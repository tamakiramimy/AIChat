import { marked } from 'marked'
import DOMPurify from 'dompurify'
import hljs from 'highlight.js'

const markdownSanitizeOptions = {
  ALLOWED_TAGS: [
    'a', 'blockquote', 'br', 'button', 'code', 'del', 'div', 'em', 'h1', 'h2',
    'h3', 'h4', 'h5', 'h6', 'hr', 'i', 'input', 'li', 'ol', 'p', 'pre', 's',
    'span', 'strong', 'sub', 'sup', 'table', 'tbody', 'td', 'th', 'thead', 'tr', 'ul'
  ],
  ALLOWED_ATTR: ['checked', 'class', 'colspan', 'disabled', 'href', 'rel', 'rowspan', 'target', 'title', 'type'],
  ALLOW_DATA_ATTR: true
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, character => {
    const escapedCharacters: Record<string, string> = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    }

    return escapedCharacters[character]
  })
}

/**
 * 全局代码块存储 - 用于存储完整的代码内容，避免 HTML 属性长度限制
 */
const codeBlockStorage = new Map<string, string>()

/**
 * 生成唯一ID用于代码块
 */
let codeBlockIdCounter = 0
function generateCodeBlockId(): string {
  return `code-block-${Date.now()}-${++codeBlockIdCounter}`
}

/**
 * 初始化 Markdown 渲染器配置
 * 配置代码高亮和其他 Markdown 选项
 */
export function initMarkdownRenderer(): void {
  const renderer = new marked.Renderer()

  renderer.code = ({ text, lang }: { text: string; lang?: string }) => {
    const codeId = generateCodeBlockId()
    const displayLang = lang || 'text'
    const escapedLanguage = escapeHtml(displayLang)
    let highlighted: string

    if (lang && hljs.getLanguage(lang)) {
      try {
        highlighted = hljs.highlight(text, { language: lang }).value
      } catch (err) {
        console.error(err)
        highlighted = hljs.highlightAuto(text).value
      }
    } else {
      highlighted = hljs.highlightAuto(text).value
    }

    // 将代码存储到全局 Map 中，避免 HTML 属性长度限制导致截断
    codeBlockStorage.set(codeId, text)

    return `<div class="code-block-wrapper">
      <div class="code-block-header">
        <span class="code-lang">${escapedLanguage}</span>
        <button class="code-copy-btn" data-code-id="${codeId}" title="复制代码">
          <i class="fas fa-copy"></i>
          <span>复制</span>
        </button>
      </div>
      <pre><code class="hljs language-${escapedLanguage}">${highlighted}</code></pre>
    </div>`
  }

  // 自定义表格渲染器 - 包装在可滚动容器中
  // marked v17+ 使用新的 token 结构
  renderer.table = (token: { header: Array<{ text: string; align: string | null }>; rows: Array<Array<{ text: string; align: string | null }>> }) => {
    // 渲染表头
    const headerCells = token.header
      .map(cell => {
        const alignStyle = cell.align ? ` style="text-align: ${cell.align}"` : ''
        return `<th${alignStyle}>${cell.text}</th>`
      })
      .join('')
    const headerRow = `<tr>${headerCells}</tr>`

    // 渲染表体
    const bodyRows = token.rows
      .map(row => {
        const cells = row
          .map(cell => {
            const alignStyle = cell.align ? ` style="text-align: ${cell.align}"` : ''
            return `<td${alignStyle}>${cell.text}</td>`
          })
          .join('')
        return `<tr>${cells}</tr>`
      })
      .join('')

    return `<div class="table-wrapper">
      <table>
        <thead>${headerRow}</thead>
        <tbody>${bodyRows}</tbody>
      </table>
    </div>`
  }

  marked.setOptions({
    renderer,
    breaks: true,
    gfm: true
  })
}

/**
 * 处理代码块复制按钮点击事件
 * 需要在组件中调用此函数来委托事件
 */
export function handleCodeBlockCopy(event: Event): void {
  const target = event.target as HTMLElement
  const copyBtn = target.closest('.code-copy-btn') as HTMLButtonElement

  if (copyBtn) {
    const codeId = copyBtn.getAttribute('data-code-id')
    if (codeId) {
      // 优先从全局 Map 获取代码（新方式）
      let code = codeBlockStorage.get(codeId)

      // 兼容旧的 data-code 方式（如果 Map 中没有）
      if (!code) {
        const encodedCode = copyBtn.getAttribute('data-code')
        if (encodedCode) {
          code = decodeURIComponent(encodedCode)
        }
      }

      if (code) {
        navigator.clipboard.writeText(code).then(() => {
          // 更新按钮状态显示复制成功
          const originalHTML = copyBtn.innerHTML
          copyBtn.innerHTML = '<i class="fas fa-check"></i><span>已复制</span>'
          copyBtn.classList.add('copied')

          setTimeout(() => {
            copyBtn.innerHTML = originalHTML
            copyBtn.classList.remove('copied')
          }, 2000)
        }).catch(err => {
          console.error('复制失败:', err)
        })
      }
    }
  }
}

/**
 * 预处理文本，移除 AI 响应中的特殊标记
 * @param text - 原始文本
 * @returns 清理后的文本
 */
function preprocessText(text: string): string {
  // 移除常见的 AI 模型特殊标记
  let cleaned = text
    .replace(/<\|begin_of_box\|>/g, '')
    .replace(/<\|end_of_box\|>/g, '')
    .replace(/<\|im_start\|>.*?<\|im_end\|>/gs, '')
    .replace(/<\|.*?\|>/g, '') // 移除其他类似格式的标记

  return cleaned
}

/**
 * 将 Markdown 文本转换为 HTML
 * @param text - Markdown 格式的文本
 * @returns HTML 格式的字符串
 */
export function formatMessage(text: string): string {
  try {
    // 预处理：移除特殊标记
    const cleanedText = preprocessText(text)
    return DOMPurify.sanitize(marked.parse(cleanedText) as string, markdownSanitizeOptions)
  } catch (error) {
    console.error('Markdown parsing error:', error)
    return DOMPurify.sanitize(escapeHtml(text).replace(/\n/g, '<br>'), markdownSanitizeOptions)
  }
}

/**
 * 格式化消息时间戳为可读字符串
 * @param timestamp - 时间戳（毫秒）
 * @returns 格式化后的时间字符串，如 "2024/01/15 14:30"
 */
export function formatMessageTime(timestamp: number): string {
  if (!timestamp) return ''
  const date = new Date(timestamp)

  const year = date.getFullYear()
  const month = (date.getMonth() + 1).toString().padStart(2, '0')
  const day = date.getDate().toString().padStart(2, '0')
  const hours = date.getHours().toString().padStart(2, '0')
  const minutes = date.getMinutes().toString().padStart(2, '0')

  return `${year}/${month}/${day} ${hours}:${minutes}`
}

/**
 * 格式化相对时间
 * @param timestamp - 时间戳（毫秒）
 * @returns 相对时间字符串，如 "刚刚"、"5分钟前"、"2小时前"
 */
export function formatRelativeTime(timestamp: number): string {
  const date = new Date(timestamp)
  const now = new Date()
  const diff = now.getTime() - date.getTime()

  if (diff < 60000) return '刚刚'
  if (diff < 3600000) return Math.floor(diff / 60000) + '分钟前'
  if (diff < 86400000) return Math.floor(diff / 3600000) + '小时前'
  if (diff < 604800000) return Math.floor(diff / 86400000) + '天前'

  return date.toLocaleDateString('zh-CN')
}

/**
 * 格式化文件大小
 * @param bytes - 文件大小（字节）
 * @returns 格式化后的大小字符串，如 "1.5 KB"、"2.3 MB"
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB'
  if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB'
  return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB'
}
