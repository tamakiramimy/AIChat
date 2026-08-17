import { ref } from 'vue'

/**
 * 录音功能 Composable
 * 提供长按录音、发送音频等功能
 */
export function useRecording() {
  const isRecording = ref(false)
  const recordingDuration = ref(0)
  const recordingError = ref<string | null>(null)

  let mediaRecorder: MediaRecorder | null = null
  let audioChunks: Blob[] = []
  let recordingTimer: ReturnType<typeof setInterval> | null = null
  let startTime: number = 0

  /**
   * 开始录音
   */
  const startRecording = async (): Promise<boolean> => {
    try {
      recordingError.value = null
      audioChunks = []

      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })

      // 尝试使用 webm 格式，如果不支持则使用其他格式
      const mimeType = MediaRecorder.isTypeSupported('audio/webm')
        ? 'audio/webm'
        : MediaRecorder.isTypeSupported('audio/mp4')
          ? 'audio/mp4'
          : 'audio/wav'

      mediaRecorder = new MediaRecorder(stream, { mimeType })

      mediaRecorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunks.push(event.data)
        }
      }

      mediaRecorder.onerror = (event) => {
        console.error('MediaRecorder error:', event)
        recordingError.value = '录音出错'
        stopRecording()
      }

      mediaRecorder.start(100) // 每100ms收集一次数据
      isRecording.value = true
      startTime = Date.now()
      recordingDuration.value = 0

      // 计时器
      recordingTimer = setInterval(() => {
        recordingDuration.value = Math.floor((Date.now() - startTime) / 1000)
      }, 1000)

      return true
    } catch (error: any) {
      console.error('Start recording error:', error)
      if (error.name === 'NotAllowedError') {
        recordingError.value = '请允许使用麦克风'
      } else if (error.name === 'NotFoundError') {
        recordingError.value = '未找到麦克风设备'
      } else {
        recordingError.value = '无法启动录音'
      }
      return false
    }
  }

  /**
   * 停止录音并返回音频 Blob
   */
  const stopRecording = (): Promise<Blob | null> => {
    return new Promise((resolve) => {
      if (!mediaRecorder || mediaRecorder.state === 'inactive') {
        isRecording.value = false
        resolve(null)
        return
      }

      // 清理计时器
      if (recordingTimer) {
        clearInterval(recordingTimer)
        recordingTimer = null
      }

      mediaRecorder.onstop = () => {
        const audioBlob = new Blob(audioChunks, { type: mediaRecorder?.mimeType || 'audio/webm' })

        // 停止所有音轨
        mediaRecorder?.stream.getTracks().forEach(track => track.stop())

        isRecording.value = false
        resolve(audioBlob)
      }

      mediaRecorder.stop()
    })
  }

  /**
   * 取消录音
   */
  const cancelRecording = () => {
    if (recordingTimer) {
      clearInterval(recordingTimer)
      recordingTimer = null
    }

    if (mediaRecorder) {
      mediaRecorder.stream.getTracks().forEach(track => track.stop())
      if (mediaRecorder.state !== 'inactive') {
        mediaRecorder.stop()
      }
    }

    isRecording.value = false
    recordingDuration.value = 0
    audioChunks = []
  }

  /**
   * 格式化录音时长
   */
  const formatDuration = (seconds: number): string => {
    const mins = Math.floor(seconds / 60)
    const secs = seconds % 60
    return `${mins}:${secs.toString().padStart(2, '0')}`
  }

  return {
    isRecording,
    recordingDuration,
    recordingError,
    startRecording,
    stopRecording,
    cancelRecording,
    formatDuration
  }
}
