const base = '/api/v1'
async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin',
    ...opts, body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text(); const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}
export const api = {
  dashboard: () => req('/dashboard'),
  campaigns: () => req('/campaigns'),
  campaign: (id) => req(`/campaigns/${id}`),
  createCampaign: (b) => req('/campaigns', { method: 'POST', body: b }),
  setStatus: (id, status) => req(`/campaigns/${id}/status`, { method: 'POST', body: { status } }),
  addPrize: (id, b) => req(`/campaigns/${id}/prizes`, { method: 'POST', body: b }),
  entries: (campaignId, result) => req(`/entries?${campaignId ? `campaignId=${campaignId}&` : ''}${result != null ? `result=${result}` : ''}`),
  setClaim: (id, status) => req(`/entries/${id}/claim`, { method: 'POST', body: { status } }),
  play: (b) => req('/play', { method: 'POST', body: b })
}
export const fmtMoney = (n) => (n ?? 0).toLocaleString('vi-VN') + 'đ'
export const fmtDate = (s) => s ? new Date(s).toLocaleDateString('vi-VN') : '—'
export const CSTATUS = ['Nháp', 'Đang chạy', 'Kết thúc']
