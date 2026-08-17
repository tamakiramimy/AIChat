<template>
  <ChatView />
</template>

<script setup lang="ts">
/**
 * App - 应用根组件
 * 作为应用的入口组件，负责加载主视图
 * 后续可在此扩展路由、全局状态等功能
 */
import ChatView from './views/ChatView.vue'
</script>

<style>
/* 全局样式重置 */
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}
</style>
