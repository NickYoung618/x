import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import App from './App.vue'

afterEach(() => vi.unstubAllGlobals())

describe('V1.3 framework status page', () => {
  it('shows the Host facts without offering device commands', async () => {
    const status = {
      architectureVersion: '1.3',
      stage: 'V13FrameworkFoundation',
      productionReady: false,
      runtimeMode: 'Unconfigured',
      unavailableCapabilities: ['TrayExecution'],
      modules: [
        { id: 'Api', layer: 'L2', owner: 'Backend', state: 'FrameworkOnly' },
        { id: 'DeviceAdapters', layer: 'L5', owner: 'Backend/PLC', state: 'LegacyEngineering' },
      ],
      capabilities: [{ id: 'TrayExecution', available: false, evidenceLevel: 'NotRun', reason: '尚未验证。' }],
    }
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => status })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(App, { global: { plugins: [createPinia()] } })
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/system/status', { cache: 'no-store' })
    expect(wrapper.text()).toContain('尚未具备生产条件')
    expect(wrapper.text()).toContain('责任模块（2）')
    expect(wrapper.text()).toContain('LegacyEngineering')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('reports a connection error and never claims readiness', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('网络不可达')))
    const wrapper = mount(App, { global: { plugins: [createPinia()] } })
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('无法连接中台')
    expect(wrapper.text()).toContain('网络不可达')
    expect(wrapper.text()).not.toContain('生产能力已验证')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('rejects an incomplete status payload instead of showing success', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({
      architectureVersion: '1.3', stage: 'Broken', runtimeMode: 'Production', productionReady: true,
      unavailableCapabilities: [], modules: [{ id: 'Api' }], capabilities: [],
    }) }))
    const wrapper = mount(App, { global: { plugins: [createPinia()] } })
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('状态接口格式不完整')
  })
})
