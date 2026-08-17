<template>
  <div class="chat-container">
    <!-- Image Viewer -->
    <ImageViewer
      :visible="imageViewerVisible"
      :fileId="imageViewerFileId"
      :src="imageViewerSrc"
      @close="closeImageViewer"
    />

    <!-- Sidebar -->
    <div class="sidebar" :class="{ 'sidebar-open': sidebarOpen }">
      <div class="sidebar-header">
        <h2><i class="fas fa-comments"></i> 聊天记录</h2>
        <button @click="toggleSidebar" class="close-sidebar-btn">
          <i class="fas fa-times"></i>
        </button>
      </div>
      <button @click="createNewChat" class="new-chat-btn">
        <i class="fas fa-plus"></i> 新聊天
      </button>
      <div class="chat-history">
        <div
          v-for="chat in chatHistory"
          :key="chat.id"
          :class="['history-item', { active: chat.id === currentChatId }]"
          @click="wrappedLoadChat(chat.id)"
        >
          <div class="history-title">{{ chat.title }}</div>
          <div class="history-time">{{ formatRelativeTime(chat.updatedAt) }}</div>
          <button @click.stop="deleteChat(chat.id)" class="delete-btn">
            <i class="fas fa-trash-alt"></i>
          </button>
        </div>
      </div>
    </div>

    <!-- Main Chat Area -->
    <div class="main-area">
      <!-- Header -->
      <div class="chat-header">
        <button @click="toggleSidebar" class="menu-btn">
          <i class="fas fa-bars"></i>
        </button>
        <h1><i class="fas fa-robot"></i> AI 助手</h1>
        <button @click="createNewChat" class="new-chat-btn-header">
          <i class="fas fa-plus"></i> 新聊天
        </button>
      </div>

      <!-- Messages Area -->
      <div class="messages-area" ref="messagesArea">
        <div
          v-for="(msg, index) in messages"
          :key="index"
          :class="['message', msg.role]"
        >
          <div class="avatar">
            <i v-if="msg.role === 'user'" class="fas fa-user"></i>
            <i v-else class="fas fa-robot"></i>
          </div>
          <div class="message-wrapper">
            <div class="message-header">
              <span class="sender-name">{{ msg.role === 'user' ? '我' : 'AI 助手' }}</span>
              <span class="message-time">{{ formatMessageTime(msg.timestamp) }}</span>
            </div>
            <div class="message-content" :class="{ 'has-audio': msg.content }">
              <div class="message-text" v-html="formatMessage(msg.content)"></div>
            </div>
            <!-- 消息操作栏（Gemini风格，用户和AI消息都显示） -->
            <div v-if="msg.content" class="message-actions">
              <button
                class="action-icon-btn"
                :class="{ active: isPlaying === `msg-${index}`, loading: isTTSLoading === `msg-${index}` }"
                @click="handleTTS(`msg-${index}`, msg.content)"
                :title="isPlaying === `msg-${index}` ? '停止播放' : '朗读'"
              >
                <i v-if="isTTSLoading === `msg-${index}`" class="fas fa-spinner fa-spin"></i>
                <i v-else-if="isPlaying === `msg-${index}`" class="fas fa-stop"></i>
                <i v-else class="fas fa-volume-up"></i>
              </button>
              <button class="action-icon-btn" @click="copyMessage(msg.content)" title="复制">
                <i class="fas fa-copy"></i>
              </button>
              <button
                v-if="msg.role === 'assistant'"
                class="action-icon-btn"
                @click="regenerateMessage(index)"
                title="重新生成"
              >
                <i class="fas fa-redo"></i>
              </button>
              <button
                v-if="msg.role === 'user'"
                class="action-icon-btn"
                @click="resendMessage(index)"
                title="重新发送"
              >
                <i class="fas fa-paper-plane"></i>
              </button>
            </div>
          </div>
        </div>

        <div v-if="isLoading" class="message assistant">
          <div class="avatar">
            <i class="fas fa-robot"></i>
          </div>
          <div class="message-wrapper">
            <div class="message-header">
              <span class="sender-name">AI 助手</span>
            </div>
            <div class="message-content">
              <div class="typing-indicator">
                <span></span>
                <span></span>
                <span></span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Input Area -->
      <div class="input-area">
        <!-- Action Buttons (Above Input) -->
        <div class="action-buttons">
          <!-- File Upload (multiple) -->
          <button class="action-btn" @click="triggerFileUpload" title="上传文件">
            <i class="fas fa-paperclip"></i>
            <input
              type="file"
              ref="fileInput"
              @change="onFileChange"
              accept="image/*,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.md"
              multiple
              style="display: none;"
            />
          </button>

          <!-- Emoji Picker -->
          <button class="action-btn" @click="toggleEmojiPicker" title="表情">
            <i class="fas fa-smile"></i>
          </button>

          <!-- Recording Button -->
          <button
            class="action-btn record-action-btn"
            :class="{ recording: isRecording }"
            @mousedown="startRecordingHandler"
            @mouseup="stopRecordingHandler"
            @mouseleave="handleRecordingMouseLeave"
            @touchstart.prevent="startRecordingHandler"
            @touchend.prevent="stopRecordingHandler"
            :disabled="isLoading"
            title="长按录音"
          >
            <i class="fas fa-microphone"></i>
          </button>
        </div>

        <!-- Attachments Preview (Images + Files combined) -->
        <div v-if="pendingImages.length > 0 || pendingFiles.length > 0" class="attachments-preview">
          <!-- Images -->
          <div
            v-for="img in pendingImages"
            :key="'img-' + img.id"
            class="preview-image-item"
          >
            <img
              :src="img.previewUrl"
              :alt="img.fileName"
              @click="previewPendingImage(img)"
              class="preview-clickable"
            />
            <button @click="removePendingImage(img.id)" class="remove-image-btn">
              <i class="fas fa-times"></i>
            </button>
          </div>
          <!-- Files -->
          <div
            v-for="file in pendingFiles"
            :key="'file-' + file.id"
            class="preview-file-item"
          >
            <div class="file-icon">
              <i :class="getFileIcon(file.fileType)"></i>
            </div>
            <div class="file-info">
              <span class="file-name">{{ file.fileName }}</span>
              <span class="file-size">{{ formatFileSize(file.size) }}</span>
            </div>
            <button @click="removePendingFile(file.id)" class="remove-file-btn">
              <i class="fas fa-times"></i>
            </button>
          </div>
        </div>

        <!-- Emoji Picker Panel -->
        <div v-show="showEmojiPicker" class="emoji-picker-container">
          <div ref="emojiPickerRef"></div>
        </div>

        <!-- Recording Indicator -->
        <div v-if="isRecording" class="recording-indicator">
          <div class="recording-pulse"></div>
          <span class="recording-text">录音中 {{ formatRecordingDuration(recordingDuration) }}</span>
          <button @click="cancelRecording" class="cancel-recording-btn">取消</button>
        </div>

        <!-- Recording Error -->
        <div v-if="recordingError" class="recording-error">
          <i class="fas fa-exclamation-circle"></i>
          {{ recordingError }}
        </div>

        <!-- Input Box -->
        <div class="input-box">
          <textarea
            v-model="inputMessage"
            @keydown.enter.exact.prevent="sendMessage"
            placeholder="输入消息(Shift+Enter 换行，Enter 发送)"
            rows="2"
            ref="textarea"
            @input="autoResize"
          ></textarea>

          <button
            @click="sendMessage"
            :disabled="!inputMessage.trim() || isLoading"
            class="send-btn-inline"
            title="发送"
          >
            <i class="fas fa-paper-plane"></i>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * ChatView - 聊天主视图组件
 * 整合聊天功能、历史记录管理和用户界面交互
 * 支持多会话并行流式传输（类似 ChatGPT）
 */
import { ref, onMounted, watch, nextTick, onUnmounted, computed } from 'vue'
import { useChat } from '../composables/useChat'
import { useChatHistory } from '../composables/useChatHistory'
import { useRecording } from '../composables/useRecording'
import { useSessionManager } from '../composables/useSessionManager'
import { useAudioPlayback } from '../composables/useAudioPlayback'
import { speechToText } from '../services/api'
import { getFile } from '../services/historyApi'
import { formatMessage, formatMessageTime, formatRelativeTime, initMarkdownRenderer, handleCodeBlockCopy } from '../utils/format'
import ImageViewer from '../components/ImageViewer.vue'
import type { Message, PendingImage, PendingFile } from '../types/chat'
import 'emoji-picker-element'
import 'highlight.js/styles/github-dark.css'
import '../styles/chat.css'

// DOM 引用
const messagesArea = ref<HTMLDivElement | null>(null)
const textarea = ref<HTMLTextAreaElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const emojiPickerRef = ref<HTMLDivElement | null>(null)

// 表情选择器状态
const showEmojiPicker = ref<boolean>(false)
let emojiPickerInstance: any = null

// 图片查看器状态
const imageViewerVisible = ref<boolean>(false)
const imageViewerFileId = ref<string>('')
const imageViewerSrc = ref<string>('')

// 初始化会话管理器（支持多会话并行）
const {
  activeSessions,
  currentSessionId: sessionManagerCurrentId,
  switchToSession,
  sendMessageToSession,
  setSaveMessageCallback,
  setOnMessagesChangeCallback,
  setOnStreamingUpdateCallback,
  getSessionStreamingStatus,
  // hasAnyActiveStream, getStreamingSessionIds 暂时未使用，保留供后续功能扩展
  setSessionMessages: sessionSetMessages
} = useSessionManager()

// 先初始化历史记录功能（需要先获取 saveMessageToServer）
const tempMessages = ref<any[]>([])
const {
  chatHistory,
  currentChatId,
  sidebarOpen,
  // isLoading: historyLoading 由 isLoading computed 替代
  saveChatHistory,
  saveMessageToServer,
  createNewChat: originalCreateNewChat,
  loadChat: originalLoadChat,
  deleteChat,
  toggleSidebar,
  initHistory
} = useChatHistory(tempMessages, () => {
  scrollToBottom()
})

// 初始化聊天功能（主要用于文件处理）
const {
  messages,
  inputMessage,
  isLoading: chatIsLoading,
  pendingImages,
  pendingFiles,
  scrollToBottom,
  handleFileUpload,
  removePendingImage,
  removePendingFile,
  getFileIconClass
} = useChat(messagesArea, () => {
  saveChatHistory()
}, saveMessageToServer)

// 计算属性：当前会话是否正在加载
const isLoading = computed(() => {
  if (!currentChatId.value) return chatIsLoading.value
  const status = getSessionStreamingStatus(currentChatId.value)
  return status.isLoading || status.isStreaming
})

// 设置保存消息回调（用于会话管理器）
setSaveMessageCallback(async (_sessionId: string, role: string, content: string, fileId?: string) => {
  // 只有当消息属于当前会话时才调用原始保存方法
  return saveMessageToServer(role, content, fileId)
})

// 设置消息变化回调
setOnMessagesChangeCallback(() => {
  saveChatHistory()
})

// 设置流式更新回调（用于同步流式消息到当前界面）
setOnStreamingUpdateCallback((sessionId: string, newMessages: Message[]) => {
  // 只有当流式会话是当前显示的会话时才更新界面
  if (sessionId === currentChatId.value) {
    isRestoringFromSessionManager = true
    messages.value = newMessages
    isRestoringFromSessionManager = false
  }
})

// 标志位：是否正在从 sessionManager 恢复消息（防止 watch 反向覆盖）
let isRestoringFromSessionManager = false

// 切换会话（不取消流式请求，支持后台继续）
const wrappedLoadChat = async (chatId: string): Promise<void> => {
  // 检查目标会话状态
  const targetSession = activeSessions.get(chatId)
  const isTargetStreaming = targetSession?.isStreaming || targetSession?.isLoading
  const targetHasMessages = targetSession && targetSession.messages.length > 0

  // 保存当前会话的状态（只保存 pending 状态，不覆盖正在流式传输的消息）
  if (currentChatId.value && currentChatId.value !== chatId) {
    const currentSession = activeSessions.get(currentChatId.value)
    const isCurrentStreaming = currentSession?.isStreaming || currentSession?.isLoading

    // 只有在当前会话不是正在流式传输时，才保存消息到 sessionManager
    // 否则会覆盖正在流式更新的消息
    switchToSession(
      currentChatId.value,
      isCurrentStreaming ? undefined : messages.value, // 流式传输中不覆盖消息
      [...pendingImages.value],
      [...pendingFiles.value],
      inputMessage.value
    )
  }

  // 设置标志位，防止 watch 覆盖 sessionManager 的消息
  isRestoringFromSessionManager = true

  if (isTargetStreaming && targetSession) {
    // 如果目标会话正在流式传输，直接使用 sessionManager 的数据
    currentChatId.value = chatId
    messages.value = [...targetSession.messages]
    sidebarOpen.value = false
  } else if (targetHasMessages) {
    // 如果目标会话在 sessionManager 中有缓存的消息（可能是刚流式完成但还没保存到服务器），使用缓存
    currentChatId.value = chatId
    messages.value = [...targetSession!.messages]
    sidebarOpen.value = false
  } else {
    // 否则从服务器加载
    await originalLoadChat(chatId)
    // 加载完成后，将消息同步到 sessionManager
    sessionSetMessages(chatId, messages.value)
  }

  isRestoringFromSessionManager = false

  // 从会话管理器恢复待发送文件和输入
  if (targetSession) {
    pendingImages.value = [...targetSession.pendingImages]
    pendingFiles.value = [...targetSession.pendingFiles]
    inputMessage.value = targetSession.inputMessage
  }

  // 同步会话ID
  sessionManagerCurrentId.value = chatId
}

// 发送消息（使用会话管理器）
const sendMessage = async (): Promise<void> => {
  if (!inputMessage.value.trim() || isLoading.value) return

  const currentSessionId = currentChatId.value
  if (!currentSessionId) return

  const userMessage = inputMessage.value.trim()
  const imagesToSend = [...pendingImages.value]
  const filesToSend = [...pendingFiles.value]

  // 清空输入
  inputMessage.value = ''
  pendingImages.value = []
  pendingFiles.value = []

  // 使用会话管理器发送消息（支持后台继续）
  await sendMessageToSession(
    currentSessionId,
    userMessage,
    imagesToSend,
    filesToSend,
    getFileIconClass,
    scrollToBottom
  )
}

// 创建新会话
const createNewChat = async (): Promise<void> => {
  // 保存当前会话状态
  if (currentChatId.value) {
    switchToSession(
      currentChatId.value,
      messages.value,
      [...pendingImages.value],
      [...pendingFiles.value],
      inputMessage.value
    )
  }

  // 创建新会话
  await originalCreateNewChat()

  // 清空输入
  pendingImages.value = []
  pendingFiles.value = []
  inputMessage.value = ''

  // 同步会话ID
  if (currentChatId.value) {
    sessionManagerCurrentId.value = currentChatId.value
  }
}

// 初始化录音功能
const {
  isRecording,
  recordingDuration,
  recordingError,
  startRecording,
  stopRecording,
  cancelRecording,
  formatDuration: formatRecordingDuration
} = useRecording()

// 初始化 TTS 播放功能
const {
  isPlaying,
  isLoading: isTTSLoading,
  playTTS
  // stopPlayback 暂时未使用，保留供后续功能扩展
} = useAudioPlayback()

// STT 处理状态
const isProcessingSTT = ref(false)

// 将 messages 绑定到 tempMessages
watch(messages, (newVal) => {
  tempMessages.value = newVal
  // 同步到会话管理器（只有在非恢复模式下才同步，防止覆盖正在流式传输的消息）
  if (currentChatId.value && !isRestoringFromSessionManager) {
    // 检查目标会话是否正在流式传输，如果是，不要用本地数据覆盖
    const session = activeSessions.get(currentChatId.value)
    if (!session?.isStreaming && !session?.isLoading) {
      sessionSetMessages(currentChatId.value, newVal)
    }
  }
}, { immediate: true, deep: true })

// 将 tempMessages 的变化同步回 messages
watch(tempMessages, (newVal) => {
  if (newVal !== messages.value) {
    messages.value = newVal
  }
}, { deep: true })

// 监听会话管理器的消息变化（用于流式更新）
watch(() => {
  const session = activeSessions.get(currentChatId.value)
  return session?.messages
}, (newMessages) => {
  if (newMessages && newMessages !== messages.value) {
    // 如果会话管理器的消息更新了，同步到本地
    const session = activeSessions.get(currentChatId.value)
    if (session?.isStreaming) {
      messages.value = [...newMessages]
    }
  }
}, { deep: true })

/**
 * 组件挂载时初始化
 */
onMounted(async () => {
  initMarkdownRenderer()
  await initHistory()

  // 初始化会话管理器的当前会话ID
  if (currentChatId.value) {
    sessionManagerCurrentId.value = currentChatId.value
    sessionSetMessages(currentChatId.value, messages.value)
  }

  // 添加图片点击事件监听
  setupImageClickHandler()

  // 添加代码块复制按钮事件监听
  setupCodeBlockCopyHandler()
})

/**
 * 组件卸载时清理
 */
onUnmounted(() => {
  if (emojiPickerInstance) {
    emojiPickerInstance.removeEventListener('emoji-click', handleEmojiClick)
  }
})

/**
 * 设置图片点击事件处理
 */
const setupImageClickHandler = () => {
  nextTick(() => {
    if (messagesArea.value) {
      messagesArea.value.addEventListener('click', (e) => {
        const target = e.target as HTMLElement
        if (target.tagName === 'IMG' && target.classList.contains('message-image-thumbnail')) {
          const fileId = target.getAttribute('data-file-id')
          const src = (target as HTMLImageElement).src

          if (fileId) {
            imageViewerFileId.value = fileId
            imageViewerSrc.value = ''
          } else if (src) {
            imageViewerSrc.value = src
            imageViewerFileId.value = ''
          }

          imageViewerVisible.value = true
        }
      })
    }
  })
}

/**
 * 设置代码块复制按钮事件处理
 */
const setupCodeBlockCopyHandler = () => {
  nextTick(() => {
    if (messagesArea.value) {
      messagesArea.value.addEventListener('click', handleCodeBlockCopy)
    }
  })
}

/**
 * 关闭图片查看器
 */
const closeImageViewer = () => {
  imageViewerVisible.value = false
  imageViewerFileId.value = ''
  imageViewerSrc.value = ''
}

/**
 * 监听消息变化，自动保存
 */
watch(() => messages.value, () => {
  saveChatHistory()
}, { deep: true })

/**
 * 自动调整文本框高度
 */
const autoResize = (): void => {
  if (textarea.value) {
    textarea.value.style.height = 'auto'
    textarea.value.style.height = Math.min(textarea.value.scrollHeight, 120) + 'px'
  }
}

/**
 * 切换表情选择器显示状态
 */
const toggleEmojiPicker = (): void => {
  showEmojiPicker.value = !showEmojiPicker.value
  // 初始化一次
  if (showEmojiPicker.value && !emojiPickerInstance) {
    nextTick(() => {
      initEmojiPicker()
    })
  }
}

/**
 * 初始化 emoji picker
 */
const initEmojiPicker = () => {
  if (emojiPickerRef.value && !emojiPickerInstance) {
    // 动态创建 emoji-picker 元素
    const picker = document.createElement('emoji-picker')
    picker.setAttribute('class', 'light')
    emojiPickerRef.value.appendChild(picker)
    emojiPickerInstance = picker

    // 监听 emoji 选择事件
    picker.addEventListener('emoji-click', handleEmojiClick)
  }
}

/**
 * 处理 emoji 点击事件
 */
const handleEmojiClick = (event: any) => {
  const emoji = event.detail.unicode
  inputMessage.value += emoji
  showEmojiPicker.value = false // 选择后自动隐藏
  textarea.value?.focus()
}

/**
 * 触发文件上传
 */
const triggerFileUpload = () => {
  fileInput.value?.click()
}

/**
 * 处理文件选择事件（支持多文件）
 * @param event - 文件选择事件
 */
const onFileChange = async (event: Event): Promise<void> => {
  const target = event.target as HTMLInputElement
  const files = target.files
  if (files && files.length > 0) {
    // 逐个处理上传的文件
    for (let i = 0; i < files.length; i++) {
      await handleFileUpload(files[i])
    }
  }
  // 清除文件输入，允许重复选择同一文件
  if (fileInput.value) {
    fileInput.value.value = ''
  }
}

/**
 * 开始录音处理
 */
const startRecordingHandler = async () => {
  if (isLoading.value || isRecording.value) return
  await startRecording()
}

/**
 * 停止录音并发送
 */
const stopRecordingHandler = async () => {
  if (!isRecording.value) return

  const audioBlob = await stopRecording()
  if (!audioBlob || audioBlob.size === 0) {
    return
  }

  // 调用 STT 转换
  isProcessingSTT.value = true
  try {
    const result = await speechToText(audioBlob)
    if (result.success && result.text) {
      // 将转换后的文字设置到输入框并发送
      inputMessage.value = result.text
      await nextTick()
      sendMessage()
    } else {
      console.error('STT failed:', result.message)
      // 可以显示错误提示
    }
  } catch (error) {
    console.error('STT error:', error)
  } finally {
    isProcessingSTT.value = false
  }
}

/**
 * 处理鼠标离开录音按钮
 */
const handleRecordingMouseLeave = () => {
  if (isRecording.value) {
    // 继续录音，不自动停止
  }
}

/**
 * 处理 TTS 播放
 */
const handleTTS = async (messageId: string, content: string) => {
  await playTTS(messageId, content)
}

/**
 * 复制消息内容到剪贴板
 */
const copyMessage = async (content: string) => {
  try {
    // 移除HTML标签获取纯文本
    const plainText = content.replace(/<[^>]*>/g, '')
    await navigator.clipboard.writeText(plainText)
    // 可以添加一个提示，这里简单处理
  } catch (error) {
    console.error('Copy failed:', error)
  }
}

/**
 * 预览待上传的图片
 */
const previewPendingImage = (img: any) => {
  imageViewerSrc.value = img.previewUrl
  imageViewerFileId.value = ''
  imageViewerVisible.value = true
}

/**
 * 重新生成AI回复（占位功能）
 */
const regenerateMessage = (index: number) => {
  // TODO: 实现重新生成功能
  console.log('Regenerate message at index:', index)
}

/**
 * 重新发送用户消息
 * 恢复文本内容和所有附件（图片和文件）
 */
const resendMessage = (index: number) => {
  const msg = messages.value[index]
  if (msg && msg.role === 'user') {
    // 提取纯文本（移除HTML标签，包括文件预览区域）
    // 移除 file-preview div 及其内容
    let textContent = msg.content
      .replace(/<div class="file-preview"[^>]*>[\s\S]*?<\/div>\s*<div>/i, '')
      .replace(/<\/div>$/i, '')
      .replace(/<[^>]*>/g, '')
      .trim()

    if (textContent) {
      inputMessage.value = textContent
    }

    // 恢复附件 - 图片
    if (msg.pendingImages && msg.pendingImages.length > 0) {
      // 优先使用保存的完整附件信息
      pendingImages.value = [...msg.pendingImages]
    } else if (msg.images && msg.images.length > 0) {
      // 从 images 字段（base64 数组）重建附件信息
      const reconstructedImages: PendingImage[] = msg.images.map((base64, idx) => {
        // 检查 base64 是否已经包含 data URL 前缀
        const hasDataPrefix = base64.startsWith('data:')
        const previewUrl = hasDataPrefix ? base64 : `data:image/png;base64,${base64}`
        const cleanBase64 = hasDataPrefix ? base64.split(',')[1] || base64 : base64

        return {
          id: `resend_${Date.now()}_${idx}`,
          fileName: `image_${idx + 1}.png`,
          base64: cleanBase64,
          previewUrl: previewUrl,
          thumbnailUrl: idx === 0 && msg.thumbnailUrl ? msg.thumbnailUrl : previewUrl,
          fileId: idx === 0 ? msg.fileId : undefined,
          size: Math.round(cleanBase64.length * 0.75) // 估算大小
        }
      })
      pendingImages.value = reconstructedImages
    } else if (msg.image) {
      // 单图片兼容（旧版消息）
      const hasDataPrefix = msg.image.startsWith('data:')
      const previewUrl = hasDataPrefix ? msg.image : `data:image/png;base64,${msg.image}`
      const cleanBase64 = hasDataPrefix ? msg.image.split(',')[1] || msg.image : msg.image

      const reconstructedImage: PendingImage = {
        id: `resend_${Date.now()}`,
        fileName: 'image.png',
        base64: cleanBase64,
        previewUrl: previewUrl,
        thumbnailUrl: msg.thumbnailUrl || previewUrl,
        fileId: msg.fileId,
        size: Math.round(cleanBase64.length * 0.75)
      }
      pendingImages.value = [reconstructedImage]
    } else {
      // 尝试从消息内容HTML中解析图片信息
      // 使用更灵活的方法：先找到所有 img 标签，然后分别提取属性
      const imgTagRegex = /<img[^>]*>/gi
      const imgMatches: Array<{fileId: string, fileName: string, thumbnailUrl: string}> = []

      let tagMatch
      while ((tagMatch = imgTagRegex.exec(msg.content)) !== null) {
        const imgTag = tagMatch[0]

        // 从 img 标签中提取各个属性
        const fileIdMatch = /data-file-id="([^"]*)"/i.exec(imgTag)
        const altMatch = /alt="([^"]*)"/i.exec(imgTag)
        const srcMatch = /src="([^"]*)"/i.exec(imgTag)

        if (fileIdMatch && fileIdMatch[1]) {
          imgMatches.push({
            fileId: fileIdMatch[1],
            fileName: altMatch ? altMatch[1] : 'image.png',
            thumbnailUrl: srcMatch ? srcMatch[1] : ''
          })
        }
      }

      if (imgMatches.length > 0) {
        // 从消息HTML中找到了图片信息，加载所有图片
        loadImagesFromServer(imgMatches)
      } else if (msg.thumbnailUrl || msg.fileId) {
        // 回退：只有单个缩略图URL和文件ID
        loadImagesFromServer([{
          fileId: msg.fileId || '',
          fileName: 'image.png',
          thumbnailUrl: msg.thumbnailUrl || ''
        }])
      }
    }

    // 恢复附件 - 文件
    if (msg.pendingFiles && msg.pendingFiles.length > 0) {
      pendingFiles.value = [...msg.pendingFiles]
    } else {
      // 尝试从消息内容HTML中解析文件信息
      const fileTagRegex = /<div class="file-attachment"[^>]*>/gi
      const fileMatches: Array<{fileId: string, fileName: string, fileType: string, mimeType: string}> = []

      let fileTagMatch
      while ((fileTagMatch = fileTagRegex.exec(msg.content)) !== null) {
        const fileTag = fileTagMatch[0]

        // 从 file-attachment div 中提取各个属性
        const fileIdMatch = /data-file-id="([^"]*)"/i.exec(fileTag)
        const fileNameMatch = /data-file-name="([^"]*)"/i.exec(fileTag)
        const fileTypeMatch = /data-file-type="([^"]*)"/i.exec(fileTag)
        const mimeTypeMatch = /data-mime-type="([^"]*)"/i.exec(fileTag)

        if (fileIdMatch && fileIdMatch[1]) {
          fileMatches.push({
            fileId: fileIdMatch[1],
            fileName: fileNameMatch ? fileNameMatch[1] : 'file',
            fileType: fileTypeMatch ? fileTypeMatch[1] : 'text',
            mimeType: mimeTypeMatch ? mimeTypeMatch[1] : 'application/octet-stream'
          })
        }
      }

      if (fileMatches.length > 0) {
        loadFilesFromServer(fileMatches)
      }
    }

    // 聚焦到输入框
    textarea.value?.focus()
  }
}

/**
 * 从服务器加载多个图片数据
 * 用于从数据库加载的消息重新发送时
 */
const loadImagesFromServer = async (imageInfos: Array<{fileId: string, fileName: string, thumbnailUrl: string}>) => {
  if (!imageInfos || imageInfos.length === 0) return

  const reconstructedImages: PendingImage[] = []

  for (let i = 0; i < imageInfos.length; i++) {
    const { fileId, fileName, thumbnailUrl } = imageInfos[i]

    try {
      if (fileId) {
        // 使用 fileId 获取完整图片数据
        const fileData = await getFile(fileId)
        if (fileData && fileData.success && fileData.base64Data) {
          reconstructedImages.push({
            id: `resend_${Date.now()}_${i}`,
            fileName: fileData.fileName || fileName,
            base64: fileData.base64Data,
            previewUrl: `data:${fileData.contentType || 'image/png'};base64,${fileData.base64Data}`,
            thumbnailUrl: thumbnailUrl || `data:${fileData.contentType || 'image/png'};base64,${fileData.base64Data}`,
            fileId: fileId,
            size: fileData.size || Math.round(fileData.base64Data.length * 0.75)
          })
          continue
        }
      }

      // 如果没有 fileId 或获取失败，使用 thumbnailUrl
      if (thumbnailUrl) {
        reconstructedImages.push({
          id: `resend_${Date.now()}_${i}`,
          fileName: fileName,
          base64: '', // 没有完整数据，发送时会使用 thumbnailUrl
          previewUrl: thumbnailUrl,
          thumbnailUrl: thumbnailUrl,
          fileId: fileId,
          size: 0
        })
      }
    } catch (error) {
      console.error(`Failed to load image ${i} from server:`, error)
      // 降级使用 thumbnailUrl
      if (thumbnailUrl) {
        reconstructedImages.push({
          id: `resend_${Date.now()}_${i}`,
          fileName: fileName,
          base64: '',
          previewUrl: thumbnailUrl,
          thumbnailUrl: thumbnailUrl,
          fileId: fileId,
          size: 0
        })
      }
    }
  }

  if (reconstructedImages.length > 0) {
    pendingImages.value = reconstructedImages
  }
}

/**
 * 从服务器加载多个文件数据
 * 用于从数据库加载的消息重新发送时
 */
const loadFilesFromServer = async (fileInfos: Array<{fileId: string, fileName: string, fileType: string, mimeType: string}>) => {
  if (!fileInfos || fileInfos.length === 0) return

  const reconstructedFiles: PendingFile[] = []

  for (let i = 0; i < fileInfos.length; i++) {
    const { fileId, fileName, fileType, mimeType } = fileInfos[i]

    try {
      if (fileId) {
        // 使用 fileId 获取完整文件数据
        const fileData = await getFile(fileId)
        if (fileData && fileData.success && fileData.base64Data) {
          reconstructedFiles.push({
            id: `resend_file_${Date.now()}_${i}`,
            fileName: fileData.fileName || fileName,
            base64: fileData.base64Data,
            fileType: fileType as PendingFile['fileType'],
            mimeType: fileData.contentType || mimeType,
            fileId: fileId,
            size: fileData.size || Math.round(fileData.base64Data.length * 0.75)
          })
        }
      }
    } catch (error) {
      console.error(`Failed to load file ${i} from server:`, error)
    }
  }

  if (reconstructedFiles.length > 0) {
    pendingFiles.value = reconstructedFiles
  }
}

/**
 * 获取文件类型图标
 */
const getFileIcon = (fileType: string): string => {
  const iconMap: Record<string, string> = {
    pdf: 'fas fa-file-pdf',
    word: 'fas fa-file-word',
    excel: 'fas fa-file-excel',
    ppt: 'fas fa-file-powerpoint',
    text: 'fas fa-file-alt'
  }
  return iconMap[fileType] || 'fas fa-file'
}

/**
 * 格式化文件大小
 */
const formatFileSize = (bytes: number): string => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}
</script>
