import { ref } from 'vue'
import type { Ref } from 'vue'
import type { Message, ChatSessionListItem } from '../types/chat'
import {
  getSessions,
  getSession,
  createSession,
  deleteSession as deleteSessionApi,
  saveMessage,
  updateSessionTitle,
  hasLocalData,
  getLocalChatHistory,
  syncLocalData,
  clearLocalChatHistory
} from '../services/historyApi'

/**
 * 聊天历史记录管理组合函数
 * 使用后端 API 进行数据持久化
 * @param messages - 当前消息列表引用
 * @param onChatLoad - 加载聊天时的回调函数
 * @returns 历史记录相关的响应式状态和方法
 */
export function useChatHistory(
  messages: Ref<Message[]>,
  onChatLoad?: () => void
) {
  /** 聊天历史列表 */
  const chatHistory = ref<ChatSessionListItem[]>([])
  /** 当前聊天会话ID */
  const currentChatId = ref<string>('')
  /** 侧边栏是否打开 */
  const sidebarOpen = ref<boolean>(false)
  /** 是否正在加载 */
  const isLoading = ref<boolean>(false)
  /** 是否正在同步数据 */
  const isSyncing = ref<boolean>(false)

  /**
   * 从服务器加载聊天历史列表
   */
  const loadChatHistory = async (): Promise<void> => {
    try {
      isLoading.value = true
      chatHistory.value = await getSessions()
    } catch (error) {
      console.error('Failed to load chat history:', error)
    } finally {
      isLoading.value = false
    }
  }

  /**
   * 保存当前会话（保存消息到服务器）
   */
  const saveChatHistory = async (): Promise<void> => {
    // 新架构下，消息在发送时即时保存，这里主要用于标题更新
    if (!currentChatId.value || messages.value.length === 0) return

    try {
      // 从第一条用户消息更新标题
      const currentChat = chatHistory.value.find(c => c.id === currentChatId.value)
      if (currentChat && currentChat.title === '新对话') {
        const firstUserMsg = messages.value.find(m => m.role === 'user')
        if (firstUserMsg) {
          const plainText = firstUserMsg.content.replace(/<[^>]*>/g, '')
          const newTitle = plainText.substring(0, 20) + (plainText.length > 20 ? '...' : '')
          await updateSessionTitle(currentChatId.value, newTitle)
          currentChat.title = newTitle
        }
      }
    } catch (error) {
      console.error('Failed to save chat history:', error)
    }
  }

  /**
   * 保存单条消息到服务器
   */
  const saveMessageToServer = async (
    role: string,
    content: string,
    fileId?: string
  ): Promise<string | undefined> => {
    if (!currentChatId.value) return undefined
    try {
      const result = await saveMessage(currentChatId.value, role, content, fileId)
      if (result.success) {
        return result.messageId
      }
    } catch (error) {
      console.error('Failed to save message:', error)
    }
    return undefined
  }

  /**
   * 创建新的聊天会话
   */
  const createNewChat = async (): Promise<void> => {
    try {
      isLoading.value = true
      const result = await createSession()

      if (result.success && result.sessionId) {
        currentChatId.value = result.sessionId

        // 加载新会话的消息
        const session = await getSession(result.sessionId)
        if (session) {
          messages.value = session.messages
        } else {
          messages.value = [{
            role: 'assistant',
            content: '你好！我是 AI 助手，有什么可以帮助你的吗？',
            timestamp: Date.now()
          }]
        }

        // 刷新会话列表
        await loadChatHistory()
        sidebarOpen.value = false
      }
    } catch (error) {
      console.error('Failed to create new chat:', error)
    } finally {
      isLoading.value = false
    }
  }

  /**
   * 加载指定的聊天会话
   * @param chatId - 要加载的聊天会话ID
   */
  const loadChat = async (chatId: string): Promise<void> => {
    try {
      isLoading.value = true
      const session = await getSession(chatId)

      if (session) {
        currentChatId.value = chatId
        messages.value = session.messages
        sidebarOpen.value = false
        onChatLoad?.()
      }
    } catch (error) {
      console.error('Failed to load chat:', error)
    } finally {
      isLoading.value = false
    }
  }

  /**
   * 删除指定的聊天会话
   * @param chatId - 要删除的聊天会话ID
   */
  const deleteChat = async (chatId: string): Promise<void> => {
    if (!confirm('确定要删除这个对话吗？')) return

    try {
      const result = await deleteSessionApi(chatId)

      if (result.success) {
        chatHistory.value = chatHistory.value.filter(c => c.id !== chatId)

        if (currentChatId.value === chatId) {
          if (chatHistory.value.length > 0) {
            await loadChat(chatHistory.value[0].id)
          } else {
            await createNewChat()
          }
        }
      }
    } catch (error) {
      console.error('Failed to delete chat:', error)
    }
  }

  /**
   * 切换侧边栏显示状态
   */
  const toggleSidebar = (): void => {
    sidebarOpen.value = !sidebarOpen.value
  }

  /**
   * 迁移本地数据到服务器
   */
  const migrateLocalData = async (): Promise<boolean> => {
    if (!hasLocalData()) return true

    try {
      isSyncing.value = true
      const localData = getLocalChatHistory()

      if (localData.length > 0) {
        const result = await syncLocalData(localData)

        if (result.success) {
          // 清除本地数据
          clearLocalChatHistory()
          console.log(`数据迁移成功: ${result.message}`)
          return true
        } else {
          console.error('数据迁移失败:', result.message)
          return false
        }
      }
      return true
    } catch (error) {
      console.error('Failed to migrate local data:', error)
      return false
    } finally {
      isSyncing.value = false
    }
  }

  /**
   * 初始化聊天历史
   * 如果有本地数据则先迁移，然后加载服务器数据
   */
  const initHistory = async (): Promise<void> => {
    try {
      isLoading.value = true

      // 检查并迁移本地数据
      if (hasLocalData()) {
        console.log('发现本地数据，开始迁移...')
        await migrateLocalData()
      }

      // 加载服务器数据
      await loadChatHistory()

      if (chatHistory.value.length === 0) {
        await createNewChat()
      } else {
        await loadChat(chatHistory.value[0].id)
      }
    } catch (error) {
      console.error('Failed to init history:', error)
      // 如果服务器不可用，创建本地会话
      messages.value = [{
        role: 'assistant',
        content: '你好！我是 AI 助手，有什么可以帮助你的吗？',
        timestamp: Date.now()
      }]
    } finally {
      isLoading.value = false
    }
  }

  return {
    chatHistory,
    currentChatId,
    sidebarOpen,
    isLoading,
    isSyncing,
    loadChatHistory,
    saveChatHistory,
    saveMessageToServer,
    createNewChat,
    loadChat,
    deleteChat,
    toggleSidebar,
    initHistory,
    migrateLocalData
  }
}
