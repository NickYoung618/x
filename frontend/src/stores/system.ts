import { defineStore } from 'pinia'
import { fetchSystemStatus, type SystemStatus } from '../api/system'

export const useSystemStore = defineStore('system', {
  state: () => ({
    status: null as SystemStatus | null,
    loading: false,
    error: null as string | null,
  }),
  actions: {
    async refresh() {
      this.loading = true
      this.error = null
      try {
        this.status = await fetchSystemStatus()
      } catch (error) {
        this.status = null
        this.error = error instanceof Error ? error.message : '无法读取系统状态'
      } finally {
        this.loading = false
      }
    },
  },
})
