/**
 * 待上传图片接口
 */
export interface PendingImage {
  /** 唯一标识 */
  id: string
  /** 文件名 */
  fileName: string
  /** base64数据 */
  base64: string
  /** 预览URL */
  previewUrl: string
  /** 服务器文件ID */
  fileId?: string
  /** 缩略图URL */
  thumbnailUrl?: string
  /** 文件大小 */
  size: number
}

/**
 * 待上传文件接口（PDF、Word、Excel、PPT等）
 */
export interface PendingFile {
  /** 唯一标识 */
  id: string
  /** 文件名 */
  fileName: string
  /** base64数据 */
  base64: string
  /** 文件类型 */
  fileType: 'pdf' | 'word' | 'excel' | 'ppt' | 'text'
  /** MIME类型 */
  mimeType: string
  /** 服务器文件ID */
  fileId?: string
  /** 提取的文本内容 */
  extractedText?: string
  /** 文件大小 */
  size: number
}

/**
 * 聊天消息接口
 */
export interface Message {
  /** 消息唯一标识 */
  id?: string
  /** 消息角色：用户或助手 */
  role: 'user' | 'assistant'
  /** 消息内容 */
  content: string
  /** 消息时间戳 */
  timestamp: number
  /** 附带的文件ID */
  fileId?: string
  /** 缩略图URL（用于显示） */
  thumbnailUrl?: string
  /** 附带的图片（base64格式，临时使用，发送后会转为fileId） */
  image?: string
  /** 多图片数据（base64格式） */
  images?: string[]
  /** 完整的待发送图片信息（用于重新发送） */
  pendingImages?: PendingImage[]
  /** 完整的待发送文件信息（用于重新发送） */
  pendingFiles?: PendingFile[]
}

/**
 * 聊天请求接口
 */
export interface ChatRequest {
  /** 用户消息内容 */
  message: string
  /** 历史消息记录 */
  history: Array<{ role: string; content: string }>
  /** 单张图片数据（base64格式，向后兼容） */
  image?: string
  /** 多张图片数据（base64格式） */
  images?: string[]
  /** 文件数据列表 */
  files?: Array<{
    fileName: string
    fileType: string
    base64Data: string
  }>
}

/**
 * 聊天会话接口
 */
export interface ChatSession {
  /** 会话唯一标识 */
  id: string
  /** 会话标题 */
  title: string
  /** 会话消息列表 */
  messages: Message[]
  /** 创建时间戳 */
  createdAt: number
  /** 最后更新时间戳 */
  updatedAt: number
  /** 消息数量 */
  messageCount?: number
}

/**
 * 文件上传请求接口
 */
export interface FileUploadRequest {
  /** 会话ID（用于缓存隔离） */
  sessionId?: string
  /** 客户端ID */
  clientId?: string
  /** 文件名 */
  fileName: string
  /** 文件类型 */
  contentType: string
  /** 文件大小（字节） */
  size: number
  /** 文件内容（base64格式） */
  base64Data: string
}

/**
 * 文件上传响应接口
 */
export interface FileUploadResponse {
  /** 是否成功 */
  success: boolean
  /** 响应消息 */
  message: string
  /** 文件ID */
  fileId?: string
  /** 文件哈希 */
  hash?: string
  /** 文件名 */
  fileName?: string
  /** 缩略图URL */
  thumbnailUrl?: string
  /** 是否命中缓存 */
  fromCache?: boolean
}

/**
 * 会话列表项接口
 */
export interface ChatSessionListItem {
  id: string
  title: string
  createdAt: number
  updatedAt: number
  messageCount: number
}

/**
 * 保存消息请求接口
 */
export interface SaveMessageRequest {
  sessionId: string
  role: string
  content: string
  fileId?: string
}

/**
 * API响应接口
 */
export interface ApiResponse {
  success: boolean
  message?: string
}

/**
 * 创建会话响应接口
 */
export interface CreateSessionResponse extends ApiResponse {
  sessionId?: string
}

/**
 * 保存消息响应接口
 */
export interface SaveMessageResponse extends ApiResponse {
  messageId?: string
}

/**
 * 文件数据响应接口
 */
export interface FileDataResponse extends ApiResponse {
  fileId?: string
  fileName?: string
  contentType?: string
  size?: number
  base64Data?: string
}

/**
 * 活跃会话状态接口（支持多会话并行）
 */
export interface ActiveSessionState {
  /** 会话ID */
  sessionId: string
  /** 消息列表 */
  messages: Message[]
  /** 是否正在加载 */
  isLoading: boolean
  /** 是否正在流式传输 */
  isStreaming: boolean
  /** 当前流式累积内容 */
  streamingContent: string
  /** 待发送的图片 */
  pendingImages: PendingImage[]
  /** 待发送的文件 */
  pendingFiles: PendingFile[]
  /** 输入框内容 */
  inputMessage: string
  /** 流式请求控制器 */
  abortController: AbortController | null
  /** 最后更新时间 */
  lastUpdated: number
}
