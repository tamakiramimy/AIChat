/**
 * 聊天历史记录 API 服务
 * 与后端 SQLite 数据库交互
 */

import type {
  ChatSession,
  ChatSessionListItem,
  Message,
  CreateSessionResponse,
  SaveMessageResponse,
  FileUploadResponse,
  FileDataResponse,
  ApiResponse
} from '../types/chat'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL

// 客户端ID，用于识别用户
let clientId: string | null = null

/**
 * 获取或生成客户端ID
 */
export function getClientId(): string {
  if (!clientId) {
    clientId = localStorage.getItem('clientId')
    if (!clientId) {
      clientId = crypto.randomUUID()
      localStorage.setItem('clientId', clientId)
    }
  }
  return clientId
}

/**
 * 获取通用请求头
 */
function getHeaders(): Record<string, string> {
  return {
    'Content-Type': 'application/json',
    'X-Client-Id': getClientId()
  }
}

/**
 * 获取会话列表
 */
export async function getSessions(): Promise<ChatSessionListItem[]> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/sessions`, {
      method: 'GET',
      headers: getHeaders()
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    return await response.json()
  } catch (error) {
    console.error('Failed to fetch sessions:', error)
    return []
  }
}

/**
 * 获取会话详情
 */
export async function getSession(sessionId: string): Promise<ChatSession | null> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/sessions/${sessionId}`, {
      method: 'GET',
      headers: getHeaders()
    })

    if (!response.ok) {
      if (response.status === 404) return null
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    return await response.json()
  } catch (error) {
    console.error('Failed to fetch session:', error)
    return null
  }
}

/**
 * 创建新会话
 */
export async function createSession(title?: string): Promise<CreateSessionResponse> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/sessions`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify({
        clientId: getClientId(),
        title
      })
    })

    return await response.json()
  } catch (error) {
    console.error('Failed to create session:', error)
    return { success: false, message: '创建会话失败' }
  }
}

/**
 * 更新会话标题
 */
export async function updateSessionTitle(sessionId: string, title: string): Promise<ApiResponse> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/sessions/${sessionId}/title`, {
      method: 'PUT',
      headers: getHeaders(),
      body: JSON.stringify({ sessionId, title })
    })

    return await response.json()
  } catch (error) {
    console.error('Failed to update session title:', error)
    return { success: false, message: '更新标题失败' }
  }
}

/**
 * 删除会话
 */
export async function deleteSession(sessionId: string): Promise<ApiResponse> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/sessions/${sessionId}`, {
      method: 'DELETE',
      headers: getHeaders()
    })

    return await response.json()
  } catch (error) {
    console.error('Failed to delete session:', error)
    return { success: false, message: '删除会话失败' }
  }
}

/**
 * 保存消息
 */
export async function saveMessage(
  sessionId: string,
  role: string,
  content: string,
  fileId?: string
): Promise<SaveMessageResponse> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/messages`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify({ sessionId, role, content, fileId })
    })

    return await response.json()
  } catch (error) {
    console.error('Failed to save message:', error)
    return { success: false, message: '保存消息失败' }
  }
}

/**
 * 上传文件到服务器
 */
export async function uploadFileToServer(
  fileName: string,
  contentType: string,
  size: number,
  base64Data: string
): Promise<FileUploadResponse> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/files`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify({
        clientId: getClientId(),
        fileName,
        contentType,
        size,
        base64Data
      })
    })

    return await response.json()
  } catch (error) {
    console.error('Failed to upload file:', error)
    return { success: false, message: '文件上传失败' }
  }
}

/**
 * 获取文件数据
 */
export async function getFile(fileId: string): Promise<FileDataResponse | null> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/files/${fileId}`, {
      method: 'GET',
      headers: getHeaders()
    })

    if (!response.ok) {
      if (response.status === 404) return null
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    return await response.json()
  } catch (error) {
    console.error('Failed to fetch file:', error)
    return null
  }
}

/**
 * 同步本地数据到服务器
 */
export async function syncLocalData(sessions: any[]): Promise<{
  success: boolean
  sessionsImported: number
  messagesImported: number
  filesImported: number
  message?: string
}> {
  try {
    const response = await fetch(`${API_BASE_URL}/history/sync`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify({
        clientId: getClientId(),
        sessions: sessions.map(s => ({
          id: s.id,
          title: s.title,
          createdAt: s.createdAt,
          updatedAt: s.updatedAt,
          messages: s.messages.map((m: Message) => ({
            role: m.role,
            content: m.content,
            imageBase64: m.image,
            timestamp: m.timestamp
          }))
        }))
      })
    })

    return await response.json()
  } catch (error) {
    console.error('Failed to sync data:', error)
    return { success: false, sessionsImported: 0, messagesImported: 0, filesImported: 0, message: '同步失败' }
  }
}

/**
 * 检查是否有本地数据需要迁移
 */
export function hasLocalData(): boolean {
  const saved = localStorage.getItem('chatHistory')
  if (!saved) return false
  try {
    const data = JSON.parse(saved)
    return Array.isArray(data) && data.length > 0
  } catch {
    return false
  }
}

/**
 * 获取本地存储的聊天历史
 */
export function getLocalChatHistory(): any[] {
  const saved = localStorage.getItem('chatHistory')
  if (!saved) return []
  try {
    return JSON.parse(saved)
  } catch {
    return []
  }
}

/**
 * 清除本地聊天历史
 */
export function clearLocalChatHistory(): void {
  localStorage.removeItem('chatHistory')
}
