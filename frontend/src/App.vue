<script setup lang="ts">
import { onMounted } from 'vue'
import { useSystemStore } from './stores/system'

const system = useSystemStore()
onMounted(() => { void system.refresh() })
</script>

<template>
  <main class="shell">
    <header class="masthead">
      <p class="eyebrow">高德缺陷检测软件中台</p>
      <h1>系统框架状态</h1>
      <p>V1.3 架构骨架 · 只读工程页面</p>
    </header>

    <p v-if="system.loading" role="status">正在读取中台状态…</p>
    <section v-else-if="system.error" class="notice error" role="alert">
      <h2>无法连接中台</h2>
      <p>{{ system.error }}</p>
      <p>请检查 Host 是否运行；页面不会自行启动检测或设备。</p>
    </section>
    <template v-else-if="system.status">
      <section class="notice" :class="system.status.productionReady ? 'ready' : 'pending'" role="status">
        <h2>{{ system.status.productionReady ? '生产能力已验证' : '尚未具备生产条件' }}</h2>
        <p>架构 {{ system.status.architectureVersion }} · {{ system.status.stage }} · 模式 {{ system.status.runtimeMode }}</p>
        <p v-if="!system.status.productionReady">当前页面只显示实际交付状态，不提供检测启动和设备控制。</p>
      </section>

      <section aria-labelledby="modules-heading">
        <h2 id="modules-heading">责任模块（{{ system.status.modules.length }}）</h2>
        <ul class="module-grid">
          <li v-for="module in system.status.modules" :key="module.id">
            <strong>{{ module.id }}</strong>
            <span>{{ module.layer }} · {{ module.owner }}</span>
            <small>{{ module.state }}</small>
          </li>
        </ul>
      </section>

      <section aria-labelledby="capabilities-heading">
        <h2 id="capabilities-heading">待接入能力</h2>
        <ul class="capability-list">
          <li v-for="capability in system.status.capabilities.filter(item => !item.available)" :key="capability.id">
            <strong>{{ capability.id }}</strong>
            <span>{{ capability.reason }}</span>
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>
