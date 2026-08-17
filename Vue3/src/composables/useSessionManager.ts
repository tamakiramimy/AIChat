import { ref, computed, reactive } from 'vue'
// Ref type is inferred from vue
import type { Message, PendingImage, PendingFile, ActiveSessionState, ChatRequest } from '../types/chat'
import { streamChat } from '../services/api'

/**
 * 会话管理器 - 支持多会话并行流式传输
 *
 * 设计理念：
 * 1. 每个会话有独立的状态（消息、流式状态、AbortController等）
 * 2. 切换会话只是切换当前显示的会话，后台流式请求继续运行
 * 3. 支持同时在多个会话中发送消息
 *
 * 类似 ChatGPT 的多会话并行体验
 */

/** 所有活跃会话的状态映射 */
const activeSessions = reactive<Map<string, ActiveSessionState>>(new Map())

/** 当前显示的会话ID */
const currentSessionId = ref<string>('')

/** 保存消息到服务器的回调 */
type SaveMessageCallback = (sessionId: string, role: string, content: string, fileId?: string) => Promise<string | undefined>

/** 保存消息回调引用 */
let saveMessageCallback: SaveMessageCallback | null = null

/** 消息变化回调 */
let onMessagesChangeCallback: (() => void) | null = null

/** 流式消息更新回调（用于同步到当前显示的会话） */
type StreamingUpdateCallback = (sessionId: string, messages: Message[]) => void
let onStreamingUpdateCallback: StreamingUpdateCallback | null = null

/**
 * 创建默认的会话状态
 */
function createDefaultSessionState(sessionId: string): ActiveSessionState {
  return {
    sessionId,
    messages: [],
    isLoading: false,
    isStreaming: false,
    streamingContent: '',
    pendingImages: [],
    pendingFiles: [],
    inputMessage: '',
    abortController: null,
    lastUpdated: Date.now()
  }
}

/**
 * 获取或创建会话状态
 */
function getOrCreateSession(sessionId: string): ActiveSessionState {
  if (!activeSessions.has(sessionId)) {
    activeSessions.set(sessionId, createDefaultSessionState(sessionId))
  }
  return activeSessions.get(sessionId)!
}

/**
 * 获取当前会话状态
 */
function getCurrentSession(): ActiveSessionState | null {
  if (!currentSessionId.value) return null
  return activeSessions.get(currentSessionId.value) || null
}

/**
 * 切换到指定会话（不取消流式请求）
 * @param sessionId - 目标会话ID
 * @param messages - 可选的消息列表（从服务器加载时使用）
 * @param currentPendingImages - 当前会话的待发送图片（切换前保存）
 * @param currentPendingFiles - 当前会话的待发送文件（切换前保存）
 * @param currentInputMessage - 当前会话的输入框内容（切换前保存）
 */
function switchToSession(
  sessionId: string,
  messages?: Message[],
  currentPendingImages?: PendingImage[],
  currentPendingFiles?: PendingFile[],
  currentInputMessage?: string
): {
  messages: Message[],
  pendingImages: PendingImage[],
  pendingFiles: PendingFile[],
  inputMessage: string,
  isLoading: boolean
} {
  // 保存当前会话状态
  const currentSession = getCurrentSession()
  if (currentSession && currentSessionId.value) {
    if (currentPendingImages !== undefined) {
      currentSession.pendingImages = currentPendingImages
    }
    if (currentPendingFiles !== undefined) {
      currentSession.pendingFiles = currentPendingFiles
    }
    if (currentInputMessage !== undefined) {
      currentSession.inputMessage = currentInputMessage
    }
  }

  // 切换到新会话
  currentSessionId.value = sessionId
  const newSession = getOrCreateSession(sessionId)

  // 如果提供了新消息，且会话不在流式传输中，更新会话消息
  // 如果会话正在流式传输，不要覆盖消息，以免丢失流式内容
  if (messages !== undefined && !newSession.isStreaming && !newSession.isLoading) {
    newSession.messages = messages
    newSession.lastUpdated = Date.now()
  }

  // 返回新会话的状态
  return {
    messages: newSession.messages,
    pendingImages: newSession.pendingImages,
    pendingFiles: newSession.pendingFiles,
    inputMessage: newSession.inputMessage,
    isLoading: newSession.isLoading
  }
}

/**
 * 更新会话消息（只有在会话不在流式传输时才更新，防止覆盖正在进行的流式消息）
 */
function setSessionMessages(sessionId: string, messages: Message[]): void {
  const session = getOrCreateSession(sessionId)
  // 如果会话正在流式传输，不要覆盖消息
  if (session.isStreaming || session.isLoading) {
    return
  }
  session.messages = messages
  session.lastUpdated = Date.now()
}

/**
 * 获取会话消息
 */
function getSessionMessages(sessionId: string): Message[] {
  const session = activeSessions.get(sessionId)
  return session?.messages ?? []
}

/**
 * 在指定会话发送消息（后台执行，不阻塞UI切换）
 */
async function sendMessageToSession(
  sessionId: string,
  userMessage: string,
  images: PendingImage[],
  files: PendingFile[],
  getFileIconClass: (fileType: PendingFile['fileType']) => string,
  scrollToBottom?: () => void
): Promise<void> {
  const session = getOrCreateSession(sessionId)

  if (!userMessage.trim() || session.isLoading) return

  // 构建消息内容
  let messageContent = userMessage
  let attachmentsHtml = ''

  if (images.length > 0) {
    const imagesHtml = images.map(img =>
      `<img src="${img.thumbnailUrl || img.previewUrl}" alt="${img.fileName}" class="message-image-thumbnail" data-file-id="${img.fileId || ''}" style="max-width: 80px; max-height: 80px; border-radius: 8px; cursor: pointer; object-fit: cover;"/>`
    ).join('')
    attachmentsHtml += imagesHtml
  }

  if (files.length > 0) {
    const filesHtml = files.map(file => {
      const iconClass = getFileIconClass(file.fileType)
      return `<div class="file-attachment" data-file-id="${file.fileId || ''}" data-file-name="${file.fileName}" data-file-type="${file.fileType}" data-mime-type="${file.mimeType}" style="display: inline-flex; align-items: center; gap: 6px; padding: 6px 10px; background: #f0f0f0; border-radius: 8px; font-size: 13px;"><i class="${iconClass}" style="color: #666;"></i><span>${file.fileName}</span></div>`
    }).join('')
    attachmentsHtml += filesHtml
  }

  if (attachmentsHtml) {
    messageContent = `<div class="file-preview" style="display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 8px;">${attachmentsHtml}</div><div>${userMessage}</div>`
  }

  // 添加用户消息
  const userMsg: Message = {
    role: 'user',
    content: messageContent,
    timestamp: Date.now(),
    fileId: images[0]?.fileId,
    thumbnailUrl: images[0]?.thumbnailUrl,
    image: images[0]?.base64,
    images: images.map(img => img.base64),
    // 保存附件信息用于重新发送
    pendingImages: images.length > 0 ? [...images] : undefined,
    pendingFiles: files.length > 0 ? [...files] : undefined
  }
  session.messages.push(userMsg)

  // 保存用户消息到服务器
  if (saveMessageCallback) {
    await saveMessageCallback(sessionId, 'user', messageContent, images[0]?.fileId)
  }

  session.isLoading = true
  session.isStreaming = true
  session.streamingContent = ''
  session.lastUpdated = Date.now()

  // 如果是当前会话，滚动到底部
  if (sessionId === currentSessionId.value && scrollToBottom) {
    scrollToBottom()
  }

  let assistantMessageIndex = -1
  let hasAddedMessage = false
  let fullAssistantContent = ''

  // 准备历史消息
  const history = session.messages.slice(-11, -1).map(msg => ({
    role: msg.role,
    content: msg.content.replace(/<[^>]*>/g, '')
  }))

  // 收集图片和文件数据
  const imagesBase64 = images.map(img => img.base64).filter(Boolean)
  const filesData = files.map(file => ({
    fileName: file.fileName,
    fileType: file.mimeType,
    base64Data: file.base64
  }))

  const requestData: ChatRequest = {
    message: userMessage,
    history: history.filter(m => m.content),
    images: imagesBase64.length > 0 ? imagesBase64 : undefined,
    files: filesData.length > 0 ? filesData : undefined
  }

  try {
    session.abortController = await streamChat(
      requestData,
      // onChunk - 即使不是当前会话也要更新消息
      (content: string) => {
        // 检查会话是否仍然活跃
        const currentSession = activeSessions.get(sessionId)
        if (!currentSession) return

        fullAssistantContent += content
        currentSession.streamingContent = fullAssistantContent

        if (!hasAddedMessage) {
          currentSession.messages.push({
            role: 'assistant',
            content: content,
            timestamp: Date.now()
          })
          assistantMessageIndex = currentSession.messages.length - 1
          hasAddedMessage = true
        } else if (assistantMessageIndex >= 0 && assistantMessageIndex < currentSession.messages.length) {
          currentSession.messages[assistantMessageIndex].content = fullAssistantContent
        }
        currentSession.lastUpdated = Date.now()

        // 如果是当前显示的会话，通知 ChatView 更新消息并滚动
        if (sessionId === currentSessionId.value) {
          if (onStreamingUpdateCallback) {
            onStreamingUpdateCallback(sessionId, [...currentSession.messages])
          }
          if (scrollToBottom) {
            scrollToBottom()
          }
        }
      },
      // onError
      (errorMessage: string) => {
        const currentSession = activeSessions.get(sessionId)
        if (!currentSession) return

        fullAssistantContent = errorMessage
        if (hasAddedMessage && assistantMessageIndex >= 0 && assistantMessageIndex < currentSession.messages.length) {
          currentSession.messages[assistantMessageIndex].content = errorMessage
        } else {
          currentSession.messages.push({
            role: 'assistant',
            content: errorMessage,
            timestamp: Date.now()
          })
        }
        currentSession.lastUpdated = Date.now()

        // 如果是当前显示的会话，通知 ChatView 更新消息
        if (sessionId === currentSessionId.value && onStreamingUpdateCallback) {
          onStreamingUpdateCallback(sessionId, [...currentSession.messages])
        }
      },
      // onComplete
      async () => {
        const currentSession = activeSessions.get(sessionId)
        if (!currentSession) return

        if (!hasAddedMessage) {
          fullAssistantContent = '抱歉，没有收到响应。请稍后重试。'
          currentSession.messages.push({
            role: 'assistant',
            content: fullAssistantContent,
            timestamp: Date.now()
          })
        }

        // 保存助手消息到服务器
        if (saveMessageCallback && fullAssistantContent) {
          await saveMessageCallback(sessionId, 'assistant', fullAssistantContent)
        }

        currentSession.isStreaming = false
        currentSession.isLoading = false
        currentSession.abortController = null
        currentSession.lastUpdated = Date.now()

        // 如果是当前显示的会话，通知 ChatView 更新消息（确保最终状态同步）
        if (sessionId === currentSessionId.value && onStreamingUpdateCallback) {
          onStreamingUpdateCallback(sessionId, [...currentSession.messages])
        }

        // 触发消息变化回调
        if (onMessagesChangeCallback) {
          onMessagesChangeCallback()
        }
      }
    )
  } catch (error) {
    console.error('Stream error:', error)
    session.isLoading = false
    session.isStreaming = false
  }
}

/**
 * 取消指定会话的流式请求
 */
function cancelSessionStream(sessionId: string): void {
  const session = activeSessions.get(sessionId)
  if (session?.abortController) {
    session.abortController.abort()
    session.abortController = null
    session.isLoading = false
    session.isStreaming = false
  }
}

/**
 * 清理会话状态（当会话被删除时调用）
 */
function removeSession(sessionId: string): void {
  cancelSessionStream(sessionId)
  activeSessions.delete(sessionId)
}

/**
 * 设置保存消息的回调
 */
function setSaveMessageCallback(callback: SaveMessageCallback): void {
  saveMessageCallback = callback
}

/**
 * 设置消息变化回调
 */
function setOnMessagesChangeCallback(callback: () => void): void {
  onMessagesChangeCallback = callback
}

/**
 * 获取会话的流式状态
 */
function getSessionStreamingStatus(sessionId: string): { isLoading: boolean; isStreaming: boolean } {
  const session = activeSessions.get(sessionId)
  return {
    isLoading: session?.isLoading ?? false,
    isStreaming: session?.isStreaming ?? false
  }
}

/**
 * 检查是否有任何会话正在流式传输
 */
function hasAnyActiveStream(): boolean {
  for (const session of activeSessions.values()) {
    if (session.isStreaming) return true
  }
  return false
}

/**
 * 获取所有正在流式传输的会话ID
 */
function getStreamingSessionIds(): string[] {
  const ids: string[] = []
  for (const [id, session] of activeSessions.entries()) {
    if (session.isStreaming) ids.push(id)
  }
  return ids
}

/**
 * 同步外部消息到会话（用于watch同步）
 */
function syncMessagesToSession(sessionId: string, messages: Message[]): void {
  const session = activeSessions.get(sessionId)
  if (session && !session.isStreaming) {
    // 只有在非流式传输状态下才同步，避免覆盖正在进行的流
    session.messages = messages
  }
}

/**
 * 设置流式更新回调（用于同步消息到 ChatView）
 */
function setOnStreamingUpdateCallback(callback: StreamingUpdateCallback): void {
  onStreamingUpdateCallback = callback
}

/**
 * 会话管理器组合函数
 */
export function useSessionManager() {
  // 当前会话状态（响应式计算属性）
  const currentSession = computed(() => getCurrentSession())

  // 当前会话的消息
  const currentMessages = computed(() => currentSession.value?.messages ?? [])

  // 当前会话是否正在加载
  const isCurrentLoading = computed(() => currentSession.value?.isLoading ?? false)

  // 当前会话是否正在流式传输
  const isCurrentStreaming = computed(() => currentSession.value?.isStreaming ?? false)

  return {
    // 状态
    activeSessions,
    currentSessionId,
    currentSession,
    currentMessages,
    isCurrentLoading,
    isCurrentStreaming,

    // 方法
    getOrCreateSession,
    switchToSession,
    setSessionMessages,
    getSessionMessages,
    sendMessageToSession,
    cancelSessionStream,
    removeSession,
    setSaveMessageCallback,
    setOnMessagesChangeCallback,
    setOnStreamingUpdateCallback,
    getSessionStreamingStatus,
    hasAnyActiveStream,
    getStreamingSessionIds,
    syncMessagesToSession
  }
}
