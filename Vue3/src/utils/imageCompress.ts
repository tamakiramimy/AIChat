/**
 * 图片压缩工具
 * 使用 browser-image-compression 库
 */
import imageCompression from 'browser-image-compression'

/** 压缩配置 */
export interface CompressOptions {
  /** 最大文件大小（字节），默认 500KB */
  maxSize?: number
  /** 最大宽度 */
  maxWidth?: number
  /** 最大高度 */
  maxHeight?: number
  /** 初始质量（0-1） */
  quality?: number
  /** 输出格式 */
  outputType?: 'image/jpeg' | 'image/png' | 'image/webp'
  /** 是否使用 Web Worker（默认 true） */
  useWebWorker?: boolean
}

/** 压缩结果 */
export interface CompressResult {
  /** 是否成功 */
  success: boolean
  /** 压缩后的 base64 数据（不含前缀） */
  base64?: string
  /** 压缩后的 DataURL（含前缀，用于预览） */
  dataUrl?: string
  /** 压缩后的大小（字节） */
  size?: number
  /** 原始大小（字节） */
  originalSize?: number
  /** 错误信息 */
  error?: string
}

/** 默认最大文件大小：500KB */
const DEFAULT_MAX_SIZE = 500 * 1024

/**
 * 将 File 转换为 DataURL
 */
function fileToDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

/**
 * 压缩图片
 * @param file 要压缩的图片文件
 * @param options 压缩选项
 * @returns 压缩结果
 */
export async function compressImage(
  file: File,
  options: CompressOptions = {}
): Promise<CompressResult> {
  const {
    maxSize = DEFAULT_MAX_SIZE,
    maxWidth = 1920,
    maxHeight = 1080,
    quality = 0.9,
    outputType = 'image/jpeg',
    useWebWorker = true
  } = options

  try {
    // 检查是否为图片文件
    if (!file.type.startsWith('image/')) {
      return {
        success: false,
        error: '不是有效的图片文件'
      }
    }

    const originalSize = file.size

    // 如果原始文件已经小于限制，直接转换为 base64
    if (originalSize <= maxSize) {
      const dataUrl = await fileToDataUrl(file)
      const base64 = dataUrl.split(',')[1]
      return {
        success: true,
        base64,
        dataUrl,
        size: originalSize,
        originalSize
      }
    }

    // 使用 browser-image-compression 压缩
    const compressedFile = await imageCompression(file, {
      maxSizeMB: maxSize / (1024 * 1024),
      maxWidthOrHeight: Math.max(maxWidth, maxHeight),
      initialQuality: quality,
      useWebWorker,
      fileType: outputType,
      // 保持 EXIF 方向信息
      preserveExif: false,
      // 使用更好的压缩算法
      alwaysKeepResolution: false
    })

    const size = compressedFile.size

    // 检查压缩结果
    if (size > maxSize) {
      // 如果仍然超过限制，尝试更激进的压缩
      const moreCompressed = await imageCompression(file, {
        maxSizeMB: maxSize / (1024 * 1024),
        maxWidthOrHeight: Math.min(maxWidth, maxHeight, 1280),
        initialQuality: 0.6,
        useWebWorker,
        fileType: 'image/jpeg'
      })

      if (moreCompressed.size > maxSize) {
        return {
          success: false,
          error: `图片过大，压缩后仍超过 ${Math.round(maxSize / 1024)}KB 限制`,
          originalSize
        }
      }

      const dataUrl = await fileToDataUrl(moreCompressed)
      const base64 = dataUrl.split(',')[1]
      return {
        success: true,
        base64,
        dataUrl,
        size: moreCompressed.size,
        originalSize
      }
    }

    const dataUrl = await fileToDataUrl(compressedFile)
    const base64 = dataUrl.split(',')[1]

    return {
      success: true,
      base64,
      dataUrl,
      size,
      originalSize
    }
  } catch (error: any) {
    return {
      success: false,
      error: error.message || '图片压缩失败'
    }
  }
}

/**
 * 格式化文件大小
 */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB'
  return (bytes / (1024 * 1024)).toFixed(2) + ' MB'
}
