<template>
  <Teleport to="body">
    <Transition name="fade">
      <div v-if="visible" class="image-viewer-overlay" @click="close">
        <div class="image-viewer-container" @click.stop>
          <button class="close-button" @click="close" title="关闭">
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>

          <div v-if="loading" class="loading-spinner">
            <div class="spinner"></div>
            <span>加载中...</span>
          </div>

          <img
            v-else
            :src="imageSrc"
            :alt="alt"
            class="viewer-image"
            @load="onImageLoad"
            @error="onImageError"
          />

          <div v-if="error" class="error-message">
            <span>图片加载失败</span>
          </div>

          <div class="image-actions">
            <button @click.stop="zoomIn" title="放大">
              <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                <line x1="11" y1="8" x2="11" y2="14"></line>
                <line x1="8" y1="11" x2="14" y2="11"></line>
              </svg>
            </button>
            <button @click.stop="zoomOut" title="缩小">
              <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                <line x1="8" y1="11" x2="14" y2="11"></line>
              </svg>
            </button>
            <button @click.stop="resetZoom" title="重置">
              <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="1 4 1 10 7 10"></polyline>
                <polyline points="23 20 23 14 17 14"></polyline>
                <path d="M20.49 9A9 9 0 0 0 5.64 5.64L1 10m22 4l-4.64 4.36A9 9 0 0 1 3.51 15"></path>
              </svg>
            </button>
            <button @click.stop="downloadImage" title="下载">
              <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { getFile } from '../services/historyApi'

const props = defineProps<{
  visible: boolean
  fileId?: string
  src?: string
  alt?: string
}>()

const emit = defineEmits<{
  (e: 'close'): void
}>()

const loading = ref(false)
const error = ref(false)
const fullImageSrc = ref('')
const scale = ref(1)

const imageSrc = computed(() => {
  return fullImageSrc.value || props.src || ''
})

watch(() => props.visible, async (newVisible) => {
  if (newVisible) {
    error.value = false
    scale.value = 1

    // 如果有 fileId，从服务器获取完整图片
    if (props.fileId && !props.src) {
      loading.value = true
      try {
        const fileData = await getFile(props.fileId)
        if (fileData?.success && fileData.base64Data) {
          fullImageSrc.value = `data:${fileData.contentType || 'image/jpeg'};base64,${fileData.base64Data}`
        } else {
          error.value = true
        }
      } catch (e) {
        console.error('Failed to load image:', e)
        error.value = true
      } finally {
        loading.value = false
      }
    } else if (props.src) {
      fullImageSrc.value = props.src
    }

    // 禁止背景滚动
    document.body.style.overflow = 'hidden'
  } else {
    document.body.style.overflow = ''
    fullImageSrc.value = ''
  }
})

const close = () => {
  emit('close')
}

const onImageLoad = () => {
  loading.value = false
}

const onImageError = () => {
  loading.value = false
  error.value = true
}

const zoomIn = () => {
  scale.value = Math.min(scale.value + 0.25, 3)
  updateImageScale()
}

const zoomOut = () => {
  scale.value = Math.max(scale.value - 0.25, 0.25)
  updateImageScale()
}

const resetZoom = () => {
  scale.value = 1
  updateImageScale()
}

const updateImageScale = () => {
  const img = document.querySelector('.viewer-image') as HTMLImageElement
  if (img) {
    img.style.transform = `scale(${scale.value})`
  }
}

const downloadImage = () => {
  if (!imageSrc.value) return

  const link = document.createElement('a')
  link.href = imageSrc.value
  link.download = props.alt || 'image.jpg'
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
}

// 监听 ESC 键关闭
const handleKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Escape' && props.visible) {
    close()
  }
}

// 组件挂载时添加键盘监听
import { onMounted, onUnmounted } from 'vue'

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  document.removeEventListener('keydown', handleKeydown)
  document.body.style.overflow = ''
})
</script>

<style scoped>
.image-viewer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.9);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
  backdrop-filter: blur(4px);
}

.image-viewer-container {
  position: relative;
  max-width: 90vw;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.close-button {
  position: absolute;
  top: -40px;
  right: -40px;
  background: rgba(255, 255, 255, 0.1);
  border: none;
  border-radius: 50%;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: white;
  transition: background-color 0.2s;
}

.close-button:hover {
  background: rgba(255, 255, 255, 0.2);
}

.viewer-image {
  max-width: 90vw;
  max-height: 80vh;
  object-fit: contain;
  border-radius: 8px;
  transition: transform 0.2s ease;
}

.loading-spinner {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  color: white;
}

.spinner {
  width: 40px;
  height: 40px;
  border: 3px solid rgba(255, 255, 255, 0.3);
  border-top-color: white;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.error-message {
  color: #ff6b6b;
  font-size: 16px;
  padding: 20px;
}

.image-actions {
  display: flex;
  gap: 8px;
  margin-top: 16px;
  padding: 8px 16px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 24px;
}

.image-actions button {
  background: transparent;
  border: none;
  color: white;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background-color 0.2s;
}

.image-actions button:hover {
  background: rgba(255, 255, 255, 0.2);
}

/* 过渡动画 */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
