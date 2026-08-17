import { ref } from 'vue'
import { textToSpeech } from '../services/api'

// 模块级变量，确保全局只有一个音频实例
let globalCurrentAudio: HTMLAudioElement | null = null
let globalIsPlaying = ref<string | null>(null)
let globalIsLoading = ref<string | null>(null)
let globalAudioCache = new Map<string, string>()

// 请求ID管理，用于取消旧请求
let requestCounter = 0
let currentRequestId: string | null = null
const SESSION_ID = 'tts-session-' + Date.now()

/**
 * TTS 播放功能 Composable
 * 提供文字转语音播放功能
 */
export function useAudioPlayback() {
  /**
   * 停止播放
   */
  const stopPlayback = () => {
    if (globalCurrentAudio) {
      globalCurrentAudio.pause()
      globalCurrentAudio.currentTime = 0
      globalCurrentAudio = null
    }
    globalIsPlaying.value = null
    // 清除当前请求ID，让正在进行的请求返回后被忽略
    currentRequestId = null
  }

  /**
   * 播放文字转语音
   * @param messageId - 消息ID
   * @param text - 要播放的文字
   */
  const playTTS = async (messageId: string, text: string): Promise<boolean> => {
    try {
      // 如果正在播放同一条消息，则停止
      if (globalIsPlaying.value === messageId) {
        stopPlayback()
        return true
      }

      // 停止当前播放（无论是哪条消息）
      stopPlayback()

      // 检查缓存
      let audioData = globalAudioCache.get(messageId)

      if (!audioData) {
        // 生成新的请求ID
        requestCounter++
        const thisRequestId = `req-${requestCounter}-${Date.now()}`
        currentRequestId = thisRequestId

        // 需要请求 TTS
        globalIsLoading.value = messageId

        // 清理 markdown 标记和特殊字符，只保留纯文本
        const cleanText = text
          .replace(/```[\s\S]*?```/g, '') // 移除代码块
          .replace(/`[^`]*`/g, '') // 移除行内代码
          .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1') // 保留链接文字
          .replace(/[#*_~>\-]/g, '') // 移除 markdown 标记
          .replace(/\n+/g, ' ') // 换行转空格
          .trim()

        if (!cleanText) {
          globalIsLoading.value = null
          currentRequestId = null
          return false
        }

        const result = await textToSpeech(cleanText, SESSION_ID, thisRequestId)

        // 检查这个请求是否仍然是最新的
        if (currentRequestId !== thisRequestId) {
          console.log('TTS request superseded, ignoring result:', thisRequestId)
          return false
        }

        globalIsLoading.value = null

        // 检查是否被后端取消
        if (result.cancelled) {
          console.log('TTS request was cancelled by backend:', thisRequestId)
          return false
        }

        if (!result.success || !result.audioData) {
          console.error('TTS failed:', result.message)
          return false
        }

        audioData = `data:${result.contentType || 'audio/mp3'};base64,${result.audioData}`
        globalAudioCache.set(messageId, audioData)
      }

      // 再次检查请求是否仍然有效（可能在缓存读取期间被取消）
      if (currentRequestId !== null && currentRequestId !== `req-${requestCounter}-${Date.now().toString().slice(0, -3)}`) {
        // 如果使用缓存，不需要检查requestId
      }

      // 播放音频
      globalCurrentAudio = new Audio(audioData)
      globalIsPlaying.value = messageId

      globalCurrentAudio.onended = () => {
        globalIsPlaying.value = null
        globalCurrentAudio = null
      }

      globalCurrentAudio.onerror = (e) => {
        console.error('Audio playback error:', e)
        globalIsPlaying.value = null
        globalCurrentAudio = null
      }

      await globalCurrentAudio.play()
      return true
    } catch (error) {
      console.error('Play TTS error:', error)
      globalIsPlaying.value = null
      globalIsLoading.value = null
      return false
    }
  }

  /**
   * 清除缓存
   */
  const clearCache = () => {
    globalAudioCache.clear()
  }

  return {
    isPlaying: globalIsPlaying,
    isLoading: globalIsLoading,
    playTTS,
    stopPlayback,
    clearCache
  }
}
