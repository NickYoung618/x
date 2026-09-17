export interface ModuleStatus {
  id: string
  layer: string
  owner: string
  state: string
}

export interface CapabilityStatus {
  id: string
  available: boolean
  evidenceLevel: string
  reason: string
}

export interface SystemStatus {
  architectureVersion: string
  stage: string
  productionReady: boolean
  runtimeMode: string
  unavailableCapabilities: string[]
  modules: ModuleStatus[]
  capabilities: CapabilityStatus[]
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null

export async function fetchSystemStatus(): Promise<SystemStatus> {
  const response = await fetch('/api/system/status', { cache: 'no-store' })
  if (!response.ok) throw new Error(`状态接口返回 HTTP ${response.status}`)

  const payload: unknown = await response.json()
  if (!isRecord(payload) ||
      typeof payload.architectureVersion !== 'string' ||
      typeof payload.stage !== 'string' ||
      typeof payload.runtimeMode !== 'string' ||
      typeof payload.productionReady !== 'boolean' ||
      !Array.isArray(payload.unavailableCapabilities) ||
      !payload.unavailableCapabilities.every(item => typeof item === 'string') ||
      !Array.isArray(payload.modules) ||
      !payload.modules.every(item => isRecord(item) && typeof item.id === 'string' &&
        typeof item.layer === 'string' && typeof item.owner === 'string' && typeof item.state === 'string') ||
      !Array.isArray(payload.capabilities) ||
      !payload.capabilities.every(item => isRecord(item) && typeof item.id === 'string' &&
        typeof item.available === 'boolean' && typeof item.evidenceLevel === 'string' &&
        typeof item.reason === 'string')) {
    throw new Error('状态接口格式不完整')
  }
  return {
    architectureVersion: payload.architectureVersion,
    stage: payload.stage,
    runtimeMode: payload.runtimeMode,
    productionReady: payload.productionReady,
    unavailableCapabilities: payload.unavailableCapabilities,
    modules: payload.modules,
    capabilities: payload.capabilities,
  }
}
