import { ref, nextTick } from 'vue'
import type { Ref } from 'vue'
import type { Message, ChatRequest, PendingImage, PendingFile } from '../types/chat'
import { streamChat } from '../services/api'
import { uploadFileToServer } from '../services/historyApi'
import { compressImage } from '../utils/imageCompress'
// useSessionManager is imported in ChatView.vue where it's used

/** 支持的文件类型和MIME类型映射 */
const FILE_TYPE_MAP: Record<string, { type: PendingFile['fileType'], icon: string }> = {
  'application/pdf': { type: 'pdf', icon: 'fas fa-file-pdf' },
  'application/msword': { type: 'word', icon: 'fas fa-file-word' },
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document': { type: 'word', icon: 'fas fa-file-word' },
  'application/vnd.ms-excel': { type: 'excel', icon: 'fas fa-file-excel' },
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': { type: 'excel', icon: 'fas fa-file-excel' },
  'application/vnd.ms-powerpoint': { type: 'ppt', icon: 'fas fa-file-powerpoint' },
  'application/vnd.openxmlformats-officedocument.presentationml.presentation': { type: 'ppt', icon: 'fas fa-file-powerpoint' },
  'text/plain': { type: 'text', icon: 'fas fa-file-alt' },
  'text/csv': { type: 'text', icon: 'fas fa-file-csv' },
  'text/markdown': { type: 'text', icon: 'fas fa-file-alt' },
}

/** 保存消息回调类型 */
type SaveMessageCallback = (role: string, content: string, fileId?: string) => Promise<string | undefined>

/**
 * 聊天功能组合函数
 * @param messagesArea - 消息区域 DOM 引用
 * @param onMessagesChange - 消息变化时的回调函数（用于保存历史）
 * @param saveMessageToServer - 保存消息到服务器的回调函数
 * @returns 聊天相关的响应式状态和方法
 */
export function useChat(
  messagesArea: Ref<HTMLDivElement | null>,
  onMessagesChange?: () => void,
  saveMessageToServer?: SaveMessageCallback
) {
  /** 消息列表 */
  const messages = ref<Message[]>([])
  /** 用户输入内容 */
  const inputMessage = ref<string>('')
  /** 是否正在加载 */
  const isLoading = ref<boolean>(false)
  /** 待发送的图片列表 */
  const pendingImages = ref<PendingImage[]>([])
  /** 待发送的文件列表（PDF、Word、Excel、PPT等） */
  const pendingFiles = ref<PendingFile[]>([])
  /** 兼容旧版单图片预览 */
  const pendingImagePreview = ref<string>('')
  /** 当前流式请求的控制器 */
  let currentStreamController: AbortController | null = null
  /** 当前流式请求的会话ID（用于验证响应归属） */
  let currentStreamSessionId: string | null = null

  /**
   * 滚动到消息区域底部
   */
  const scrollToBottom = (): void => {
    nextTick(() => {
      if (messagesArea.value) {
        messagesArea.value.scrollTop = messagesArea.value.scrollHeight
      }
    })
  }

  /**
   * 设置消息列表
   * @param newMessages - 新的消息列表
   */
  const setMessages = (newMessages: Message[]): void => {
    messages.value = newMessages
  }

  /**
   * 取消当前正在进行的流式请求
   */
  const cancelCurrentStream = (): void => {
    if (currentStreamController) {
      currentStreamController.abort()
      currentStreamController = null
      currentStreamSessionId = null
      isLoading.value = false
    }
  }

  /**
   * 设置当前流式请求的会话ID
   * @param sessionId - 会话ID
   */
  const setStreamSessionId = (sessionId: string): void => {
    currentStreamSessionId = sessionId
  }

  /**
   * 发送消息
   * @param sessionId - 可选的会话ID，用于验证流式响应归属
   * @returns Promise<void>
   */
  const sendMessage = async (sessionId?: string): Promise<void> => {
    if (!inputMessage.value.trim() || isLoading.value) return

    // 取消之前的流式请求
    cancelCurrentStream()

    // 设置当前会话ID
    if (sessionId) {
      currentStreamSessionId = sessionId
    }

    const userMessage = inputMessage.value.trim()
    const imagesToSend = [...pendingImages.value]
    const filesToSend = [...pendingFiles.value]

    inputMessage.value = ''
    pendingImages.value = []
    pendingFiles.value = []
    pendingImagePreview.value = ''

    // 构建消息内容（可能包含多个图片和文件）
    let messageContent = userMessage
    let attachmentsHtml = ''

    if (imagesToSend.length > 0) {
      const imagesHtml = imagesToSend.map(img =>
        `<img src="${img.thumbnailUrl || img.previewUrl}" alt="${img.fileName}" class="message-image-thumbnail" data-file-id="${img.fileId || ''}" style="max-width: 80px; max-height: 80px; border-radius: 8px; cursor: pointer; object-fit: cover;"/>`
      ).join('')
      attachmentsHtml += imagesHtml
    }

    if (filesToSend.length > 0) {
      const filesHtml = filesToSend.map(file => {
        const iconClass = getFileIconClass(file.fileType)
        return `<div class="file-attachment" style="display: inline-flex; align-items: center; gap: 6px; padding: 6px 10px; background: #f0f0f0; border-radius: 8px; font-size: 13px;"><i class="${iconClass}" style="color: #666;"></i><span>${file.fileName}</span></div>`
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
      fileId: imagesToSend[0]?.fileId,
      thumbnailUrl: imagesToSend[0]?.thumbnailUrl,
      image: imagesToSend[0]?.base64,
      images: imagesToSend.map(img => img.base64),
      // 保存完整的附件信息用于重新发送
      pendingImages: imagesToSend.length > 0 ? [...imagesToSend] : undefined,
      pendingFiles: filesToSend.length > 0 ? [...filesToSend] : undefined
    }
    messages.value.push(userMsg)

    // 保存用户消息到服务器
    if (saveMessageToServer) {
      await saveMessageToServer('user', messageContent, imagesToSend[0]?.fileId)
    }

    scrollToBottom()
    isLoading.value = true
    onMessagesChange?.()

    let assistantMessageIndex = -1
    let hasAddedMessage = false
    let fullAssistantContent = ''

    // 准备历史消息（最近10条，不包括当前用户消息）
    const history = messages.value.slice(-11, -1).map(msg => ({
      role: msg.role,
      content: msg.content.replace(/<[^>]*>/g, '') // 移除HTML标签
    }))

    // 收集所有图片的base64数据
    const imagesBase64 = imagesToSend.map(img => img.base64).filter(Boolean)

    // 收集所有文件数据
    const filesData = filesToSend.map(file => ({
      fileName: file.fileName,
      fileType: file.mimeType,
      base64Data: file.base64
    }))

    const requestData: ChatRequest = {
      message: userMessage,
      history: history.filter(m => m.content),
      // 只使用images字段，不再使用image字段避免重复
      images: imagesBase64.length > 0 ? imagesBase64 : undefined,
      files: filesData.length > 0 ? filesData : undefined
    }

    // 保存发送时的会话ID，用于验证响应归属
    const streamSessionId = currentStreamSessionId

    currentStreamController = await streamChat(
      requestData,
      // onChunk
      (content: string) => {
        // 检查会话ID是否仍然匹配（用户可能已切换到其他会话）
        if (streamSessionId && currentStreamSessionId !== streamSessionId) {
          return // 会话已切换，忽略此响应
        }

        fullAssistantContent += content
        if (!hasAddedMessage) {
          messages.value.push({
            role: 'assistant',
            content: content,
            timestamp: Date.now()
          })
          assistantMessageIndex = messages.value.length - 1
          hasAddedMessage = true
        } else if (assistantMessageIndex >= 0 && assistantMessageIndex < messages.value.length) {
          messages.value[assistantMessageIndex].content += content
        }
        scrollToBottom()
      },
      // onError
      (errorMessage: string) => {
        // 检查会话ID是否仍然匹配
        if (streamSessionId && currentStreamSessionId !== streamSessionId) {
          return
        }

        fullAssistantContent = errorMessage
        if (hasAddedMessage && assistantMessageIndex >= 0 && assistantMessageIndex < messages.value.length) {
          messages.value[assistantMessageIndex].content = errorMessage
        } else {
          messages.value.push({
            role: 'assistant',
            content: errorMessage,
            timestamp: Date.now()
          })
        }
      },
      // onComplete
      async () => {
        // 检查会话ID是否仍然匹配
        if (streamSessionId && currentStreamSessionId !== streamSessionId) {
          return
        }

        if (!hasAddedMessage) {
          fullAssistantContent = '抱歉，没有收到响应。请稍后重试。'
          messages.value.push({
            role: 'assistant',
            content: fullAssistantContent,
            timestamp: Date.now()
          })
        }

        // 保存助手消息到服务器
        if (saveMessageToServer && fullAssistantContent) {
          await saveMessageToServer('assistant', fullAssistantContent)
        }

        currentStreamController = null
        onMessagesChange?.()
      }
    )

    isLoading.value = false
    scrollToBottom()
  }

  /**
   * 获取文件类型图标CSS类
   */
  const getFileIconClass = (fileType: PendingFile['fileType']): string => {
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
   * 将文件转换为 base64
   */
  const fileToBase64 = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader()
      reader.onload = () => {
        const result = reader.result as string
        const base64 = result.split(',')[1]
        resolve(base64)
      }
      reader.onerror = reject
      reader.readAsDataURL(file)
    })
  }

  /**
   * 处理文件上传（支持图片和文档）
   * @param file - 要上传的文件
   */
  const handleFileUpload = async (file: File): Promise<void> => {
    if (!file) return

    const isImage = file.type.startsWith('image/')
    const fileTypeInfo = FILE_TYPE_MAP[file.type]

    // 检查是否是支持的文件类型
    if (!isImage && !fileTypeInfo) {
      messages.value.push({
        role: 'assistant',
        content: '不支持此文件类型。支持的文件类型：图片、PDF、Word、Excel、PPT、TXT、CSV、Markdown',
        timestamp: Date.now()
      })
      return
    }

    // 文件大小限制：20MB
    const maxFileSize = 20 * 1024 * 1024
    if (file.size > maxFileSize) {
      messages.value.push({
        role: 'assistant',
        content: '文件大小超过限制（最大 20MB）',
        timestamp: Date.now()
      })
      return
    }

    try {
      if (isImage) {
        // 处理图片
        if (pendingImages.value.length >= 5) {
          messages.value.push({
            role: 'assistant',
            content: '最多只能上传5张图片。',
            timestamp: Date.now()
          })
          return
        }

        // 压缩图片（限制 500KB）
        const result = await compressImage(file, {
          maxSize: 500 * 1024,
          maxWidth: 1920,
          maxHeight: 1080,
          quality: 0.85,
          outputType: 'image/jpeg'
        })

        if (!result.success) {
          messages.value.push({
            role: 'assistant',
            content: result.error || '图片处理失败，请尝试上传更小的图片。',
            timestamp: Date.now()
          })
          return
        }

        // 上传到服务器
        const uploadResult = await uploadFileToServer(
          file.name,
          'image/jpeg',
          result.size!,
          result.base64!
        )

        // 创建唯一ID
        const imageId = `img-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`

        // 添加到待发送图片列表
        const newImage: PendingImage = {
          id: imageId,
          fileName: file.name,
          base64: result.base64!,
          previewUrl: result.dataUrl!,
          fileId: uploadResult.success ? uploadResult.fileId : undefined,
          thumbnailUrl: uploadResult.success ? uploadResult.thumbnailUrl : undefined,
          size: result.size!
        }

        pendingImages.value.push(newImage)
        pendingImagePreview.value = result.dataUrl!
      } else {
        // 处理文档文件
        if (pendingFiles.value.length >= 5) {
          messages.value.push({
            role: 'assistant',
            content: '最多只能上传5个文档文件。',
            timestamp: Date.now()
          })
          return
        }

        // 转换为 base64
        const base64Data = await fileToBase64(file)

        // 上传到服务器获取真实fileId
        const uploadResult = await uploadFileToServer(
          file.name,
          file.type,
          file.size,
          base64Data
        )

        // 创建唯一ID
        const localId = `file-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`

        // 添加到待发送文件列表
        const newFile: PendingFile = {
          id: localId,
          fileName: file.name,
          base64: base64Data,
          fileType: fileTypeInfo!.type,
          mimeType: file.type,
          size: file.size,
          fileId: uploadResult.success ? uploadResult.fileId : undefined
        }

        pendingFiles.value.push(newFile)
      }
    } catch (error) {
      console.error('File processing error:', error)
      messages.value.push({
        role: 'assistant',
        content: '文件处理失败，请重试。',
        timestamp: Date.now()
      })
    }
  }

  /**
   * 删除指定的待发送图片
   * @param imageId - 要删除的图片ID
   */
  const removePendingImage = (imageId: string): void => {
    pendingImages.value = pendingImages.value.filter(img => img.id !== imageId)
    if (pendingImages.value.length === 0) {
      pendingImagePreview.value = ''
    }
  }

  /**
   * 删除指定的待发送文件
   * @param fileId - 要删除的文件ID
   */
  const removePendingFile = (fileId: string): void => {
    pendingFiles.value = pendingFiles.value.filter(f => f.id !== fileId)
  }

  /**
   * 清除所有待发送的图片
   */
  const clearPendingImage = (): void => {
    pendingImages.value = []
    pendingImagePreview.value = ''
  }

  /**
   * 清除所有待发送的文件
   */
  const clearPendingFiles = (): void => {
    pendingFiles.value = []
  }

  return {
    messages,
    inputMessage,
    isLoading,
    pendingImages,
    pendingFiles,
    pendingImagePreview,
    scrollToBottom,
    setMessages,
    sendMessage,
    handleFileUpload,
    removePendingImage,
    removePendingFile,
    clearPendingImage,
    clearPendingFiles,
    getFileIconClass,
    cancelCurrentStream,
    setStreamSessionId
  }
}
