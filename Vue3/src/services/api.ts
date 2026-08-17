import type { ChatRequest, FileUploadRequest, FileUploadResponse } from '../types/chat'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL

/**
 * 发送流式聊天请求
 * @param requestData - 聊天请求数据
 * @param onChunk - 接收到数据块时的回调函数
 * @param onError - 发生错误时的回调函数
 * @param onComplete - 流式传输完成时的回调函数
 * @returns AbortController 用于取消请求
 */
export async function streamChat(
  requestData: ChatRequest,
  onChunk: (content: string) => void,
  onError: (error: string) => void,
  onComplete: () => void
): Promise<AbortController> {
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), 600000) // 10分钟超时

  try {
    const response = await fetch(`${API_BASE_URL}/chat/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'text/event-stream',
        'Cache-Control': 'no-cache',
      },
      body: JSON.stringify(requestData),
      signal: controller.signal,
      cache: 'no-store'
    })

    clearTimeout(timeoutId)

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    if (!response.body) {
      throw new Error('No response body')
    }

    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()

      if (done) {
        break
      }

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() || ''

      for (const line of lines) {
        if (line.trim() === '') continue

        if (line.startsWith('data: ')) {
          const data = line.substring(6).trim()

          if (data === '[DONE]') {
            continue
          }

          try {
            const parsed = JSON.parse(data)

            // 处理不同的响应格式
            let content = ''
            if (parsed.choices && parsed.choices[0]) {
              // OpenAI 格式
              content = parsed.choices[0].delta?.content || parsed.choices[0].text || ''
            } else if (parsed.content) {
              // 简单格式
              content = parsed.content
            } else if (typeof parsed === 'string') {
              content = parsed
            }

            if (content) {
              onChunk(content)
            }
          } catch (e) {
            // 如果不是 JSON，当作纯文本处理
            if (data && data !== '[DONE]') {
              onChunk(data)
            }
          }
        }
      }
    }

    onComplete()
  } catch (error: any) {
    clearTimeout(timeoutId)

    let errorMessage = '抱歉，发生了错误。'

    if (error.name === 'AbortError') {
      errorMessage = '请求超时，请稍后重试。'
    } else if (error.message.includes('Failed to fetch')) {
      errorMessage = '网络连接失败，请检查网络设置。'
    } else if (error.message.includes('HTTP error')) {
      errorMessage = `服务器错误：${error.message}`
    }

    onError(errorMessage)
  }

  return controller
}

/**
 * 将文件转换为 base64 格式
 * @param file - 要转换的文件
 * @returns Promise<string> base64 编码的文件内容
 */
export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result as string
      // 移除 data:xxx;base64, 前缀，只保留 base64 数据
      const base64 = result.split(',')[1]
      resolve(base64)
    }
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

/**
 * 获取文件的 Data URL（用于预览）
 * @param file - 要转换的文件
 * @returns Promise<string> Data URL 格式的文件内容
 */
export function fileToDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

/**
 * 上传文件（使用 base64 格式）
 * @param file - 要上传的文件
 * @param sessionId - 可选的会话ID，用于缓存隔离
 * @returns Promise<FileUploadResponse> 上传结果
 */
export async function uploadFile(file: File, sessionId?: string): Promise<FileUploadResponse> {
  try {
    const base64Data = await fileToBase64(file)

    const requestData: FileUploadRequest = {
      sessionId: sessionId,
      fileName: file.name,
      contentType: file.type,
      size: file.size,
      base64Data: base64Data
    }

    const response = await fetch(`${API_BASE_URL}/chat/upload`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(requestData)
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    return await response.json()
  } catch (error: any) {
    console.error('Upload error:', error)
    return {
      success: false,
      message: '文件上传失败，请稍后重试。'
    }
  }
}

/**
 * 语音转文字 (STT)
 * @param audioBlob - 音频 Blob 数据
 * @returns Promise<{ success: boolean, text?: string, message?: string }>
 */
export async function speechToText(audioBlob: Blob): Promise<{ success: boolean, text?: string, message?: string }> {
  try {
    const base64Data = await blobToBase64(audioBlob)

    const response = await fetch(`${API_BASE_URL}/audio/stt/base64`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        audioData: base64Data,
        contentType: audioBlob.type
      })
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    return await response.json()
  } catch (error: any) {
    console.error('STT error:', error)
    return {
      success: false,
      message: '语音转文字失败，请稍后重试。'
    }
  }
}

/**
 * 文字转语音 (TTS)
 * @param text - 要转换的文字
 * @param sessionId - 会话ID
 * @param requestId - 请求ID
 * @returns Promise<{ success: boolean, audioData?: string, contentType?: string, message?: string, requestId?: string, cancelled?: boolean }>
 */
export async function textToSpeech(
  text: string,
  sessionId?: string,
  requestId?: string
): Promise<{
  success: boolean,
  audioData?: string,
  contentType?: string,
  message?: string,
  requestId?: string,
  cancelled?: boolean
}> {
  try {
    const response = await fetch(`${API_BASE_URL}/audio/tts`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ text, sessionId, requestId })
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    return await response.json()
  } catch (error: any) {
    console.error('TTS error:', error)
    return {
      success: false,
      message: '文字转语音失败，请稍后重试。'
    }
  }
}

/**
 * 将 Blob 转换为 base64 格式
 * @param blob - 要转换的 Blob
 * @returns Promise<string> base64 编码的数据（不含前缀）
 */
export function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result as string
      const base64 = result.split(',')[1]
      resolve(base64)
    }
    reader.onerror = reject
    reader.readAsDataURL(blob)
  })
}
