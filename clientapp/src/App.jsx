import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Outlet } from 'react-router-dom'
import { api, fmtMoney, fmtDate, CSTATUS } from './api'

function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Modal({ title, onClose, wide, children }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" style={wide ? { maxWidth: 720 } : undefined} onClick={e => e.stopPropagation()}>
        <div className="row" style={{ marginBottom: 12 }}><h2 style={{ flex: 1, margin: 0 }}>{title}</h2>
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={onClose}>Đóng</button></div>{children}
      </div>
    </div>
  )
}
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }

function Layout() {
  return (
    <>
      <nav className="nav"><span className="brand">🎁 MiniPromo</span>
        <NavLink to="/" end>Tổng quan</NavLink><NavLink to="/campaigns">Chiến dịch</NavLink>
        <NavLink to="/entries">Lượt chơi</NavLink><NavLink to="/vouchers">Mã giảm giá</NavLink>
        <NavLink to="/play">Chơi thử</NavLink><NavLink to="/redeem">Dùng voucher</NavLink></nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

function Dashboard() {
  const [d, setD] = useState(null); const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  return (
    <>
      <h1>Tổng quan khuyến mãi {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis" style={{ marginBottom: 18 }}>
        <div className="kpi"><div className="v">{d.campaigns}</div><div className="l">Chiến dịch</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--success)' }}>{d.running}</div><div className="l">Đang chạy</div></div>
        <div className="kpi"><div className="v">{d.totalPlays}</div><div className="l">Lượt chơi</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--warning)' }}>{d.totalWins}</div><div className="l">Lượt trúng</div></div>
        <div className="kpi"><div className="v" style={{ fontSize: 18, color: 'var(--success)' }}>{fmtMoney(d.valueAwarded)}</div><div className="l">Giá trị đã trao</div></div>
      </div>
      <div className="card"><h2>Top chiến dịch</h2>
        <table><thead><tr><th>Chiến dịch</th><th className="right">Lượt chơi</th><th className="right">Trúng</th><th className="right">Giá trị trao</th></tr></thead>
          <tbody>{d.top.map((t, i) => <tr key={i}><td>{t.campaign}</td><td className="right">{t.plays}</td><td className="right">{t.wins}</td><td className="right">{fmtMoney(t.valueAwarded)}</td></tr>)}</tbody></table>
      </div>
    </>
  )
}

function Campaigns() {
  const [rows, setRows] = useState([]); const [open, setOpen] = useState(null); const [show, setShow] = useState(false)
  const load = () => api.campaigns().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 1 }}>Chiến dịch</h1><button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Tạo chiến dịch</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Mã</th><th>Tên</th><th>Thời gian</th><th className="right">Giải</th><th>Trạng thái</th></tr></thead>
          <tbody>{rows.map(c => (
            <tr key={c.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(c.id)}>
              <td style={{ fontFamily: 'monospace' }}>{c.code}</td><td>{c.name}</td><td>{fmtDate(c.fromDate)} → {fmtDate(c.toDate)}</td>
              <td className="right">{c.prizes}</td><td><Badge text={c.statusText} css={c.statusCss} />{c.live && <span className="badge success" style={{ marginLeft: 4 }}>LIVE</span>}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={5} className="muted" style={{ padding: 20 }}>Chưa có chiến dịch.</td></tr>}</tbody></table>
      </div>
      {open && <CampaignDetail id={open} onClose={() => setOpen(null)} onChanged={load} />}
      {show && <CampaignForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function CampaignDetail({ id, onClose, onChanged }) {
  const [c, setC] = useState(null); const [msg, setMsg] = useState(null); const [pf, setPf] = useState({ name: '', tier: '', value: 0, quantity: 1, weight: 1 })
  const load = () => api.campaign(id).then(r => setC(r.data))
  useEffect(() => { load() }, [id])
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 3000) }
  const act = async (fn, ok) => { try { const r = await fn(); flash(true, ok || r.data?.msg || 'OK'); load(); onChanged() } catch (e) { flash(false, e.message) } }
  const addPrize = async () => { try { await api.addPrize(id, { ...pf, value: Number(pf.value), quantity: Number(pf.quantity), weight: Number(pf.weight) }); setPf({ name: '', tier: '', value: 0, quantity: 1, weight: 1 }); flash(true, 'Đã thêm giải.'); load() } catch (e) { flash(false, e.message) } }
  if (!c) return <Modal title="…" onClose={onClose}><p className="muted">Đang tải…</p></Modal>
  return (
    <Modal title={`${c.name} (${c.code})`} onClose={onClose} wide>
      <Flash msg={msg} />
      <div className="row" style={{ marginBottom: 8 }}><Badge text={c.statusText} css="secondary" />{c.live && <Badge text="LIVE" css="success" />}
        <span className="pill" style={{ flex: 'none' }}>{c.plays} lượt · {c.wins} trúng · {fmtMoney(c.valueAwarded)}</span></div>
      <div className="section-t">Cơ cấu giải</div>
      <table><thead><tr><th>Hạng</th><th>Giải</th><th className="right">Giá trị</th><th className="right">Còn/Tổng</th><th className="right">Trọng số</th></tr></thead>
        <tbody>{c.prizes.map(p => <tr key={p.id}><td>{p.tier}</td><td>{p.name}</td><td className="right">{fmtMoney(p.value)}</td><td className="right">{p.remaining}/{p.quantity}</td><td className="right">{p.weight}</td></tr>)}</tbody></table>
      {c.status === 0 && (
        <div className="card" style={{ background: '#f8fafc', marginTop: 8 }}>
          <div className="section-t">Thêm giải</div>
          <div className="row"><Field label="Hạng"><input value={pf.tier} onChange={e => setPf({ ...pf, tier: e.target.value })} /></Field>
            <Field label="Tên giải"><input value={pf.name} onChange={e => setPf({ ...pf, name: e.target.value })} /></Field></div>
          <div className="row"><Field label="Giá trị"><input type="number" value={pf.value} onChange={e => setPf({ ...pf, value: e.target.value })} /></Field>
            <Field label="Số suất"><input type="number" value={pf.quantity} onChange={e => setPf({ ...pf, quantity: e.target.value })} /></Field>
            <Field label="Trọng số"><input type="number" value={pf.weight} onChange={e => setPf({ ...pf, weight: e.target.value })} /></Field></div>
          <div style={{ marginTop: 10 }}><button className="btn sm" onClick={addPrize} disabled={!pf.name}>+ Thêm giải</button></div>
        </div>
      )}
      <div className="row" style={{ gap: 6, marginTop: 12 }}>
        {c.status === 0 && <button className="btn sm" onClick={() => act(() => api.setStatus(id, 1), 'Đã chạy chiến dịch.')} disabled={c.prizes.length === 0}>Chạy chiến dịch</button>}
        {c.status === 1 && <button className="btn gray sm" onClick={() => act(() => api.setStatus(id, 2), 'Đã kết thúc.')}>Kết thúc</button>}
        {c.status === 0 && c.prizes.length === 0 && <span className="muted" style={{ alignSelf: 'center', fontSize: 12 }}>Cần ≥1 giải để chạy</span>}
      </div>
    </Modal>
  )
}

function CampaignForm({ onClose, onSaved }) {
  const [f, setF] = useState({ name: '', code: '', description: '', loseWeight: 100 }); const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { if (!f.name) { setErr('Cần tên'); return } await api.createCampaign({ ...f, loseWeight: Number(f.loseWeight) }); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Tạo chiến dịch" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Tên *"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
        <Field label="Mã công khai"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field></div>
      <Field label="Mô tả"><input value={f.description} onChange={e => up('description', e.target.value)} /></Field>
      <Field label="Trọng số 'không trúng'"><input type="number" value={f.loseWeight} onChange={e => up('loseWeight', e.target.value)} /></Field>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Tạo (Nháp)</button></div>
    </Modal>
  )
}

function Entries() {
  const [rows, setRows] = useState([]); const [win, setWin] = useState(''); const [msg, setMsg] = useState(null)
  const load = () => api.entries(null, win === '' ? null : Number(win)).then(r => setRows(r.data))
  useEffect(() => { load() }, [win])
  const claim = async (id, status) => { try { await api.setClaim(id, status); setMsg({ ok: true, text: 'Đã cập nhật.' }); load() } catch (e) { setMsg({ ok: false, text: e.message }) } }
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 'none' }}>Lượt chơi</h1><div className="sp" />
        <select style={{ maxWidth: 160 }} value={win} onChange={e => setWin(e.target.value)}><option value="">— Tất cả —</option><option value="1">Trúng</option><option value="0">Trượt</option></select></div>
      <Flash msg={msg} />
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Chiến dịch</th><th>Mã</th><th>Khách</th><th>Kết quả</th><th>Trao thưởng</th></tr></thead>
          <tbody>{rows.map(e => (
            <tr key={e.id}><td>{e.campaign}</td><td style={{ fontFamily: 'monospace' }}>{e.code}</td><td>{e.customerName || '—'}{e.phone ? ` · ${e.phone}` : ''}</td>
              <td>{e.win ? <Badge text={`🎉 ${e.prizeName}`} css="success" /> : <span className="muted">Trượt</span>}</td>
              <td>{e.win && (e.claim === 1 ? <button className="btn sm" style={{ flex: 'none' }} onClick={() => claim(e.id, 2)}>Đánh dấu đã trao</button> : e.claim === 2 ? <Badge text="Đã trao" css="success" /> : <span className="muted">—</span>)}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={5} className="muted" style={{ padding: 20 }}>Chưa có lượt chơi.</td></tr>}</tbody></table>
      </div>
    </>
  )
}

function Play() {
  const [f, setF] = useState({ campaignCode: '', code: '', name: '', phone: '' }); const [res, setRes] = useState(null); const [err, setErr] = useState(null)
  const up = (k, v) => setF({ ...f, [k]: v })
  const doPlay = async () => { try { const r = await api.play(f); setRes(r.data); setErr(null) } catch (e) { setErr(e.message); setRes(null) } }
  return (
    <>
      <h1>Chơi thử (mô phỏng người tiêu dùng)</h1>
      <div className="card">
        <div className="row"><Field label="Mã chiến dịch"><input value={f.campaignCode} onChange={e => up('campaignCode', e.target.value)} /></Field>
          <Field label="Mã tem/QR (mỗi mã chơi 1 lần)"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field></div>
        <div className="row"><Field label="Tên"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
          <Field label="SĐT"><input value={f.phone} onChange={e => up('phone', e.target.value)} /></Field></div>
        <div style={{ marginTop: 12 }}><button className="btn" onClick={doPlay}>🎡 Quay</button></div>
      </div>
      {err && <Flash msg={{ ok: false, text: err }} />}
      {res && (
        <div className="card" style={{ borderLeft: `5px solid ${res.win ? 'var(--success)' : 'var(--muted)'}`, textAlign: 'center' }}>
          <h2 style={{ color: res.win ? 'var(--success)' : 'var(--ink)' }}>{res.win ? `🎉 CHÚC MỪNG! ${res.prize}` : '😔 Chúc bạn may mắn lần sau'}</h2>
          {res.win && <p>Giá trị: {fmtMoney(res.value)}</p>}
          <p className="muted">{res.msg}</p>
        </div>
      )}
    </>
  )
}

function Vouchers() {
  const [rows, setRows] = useState([]); const [open, setOpen] = useState(null); const [show, setShow] = useState(false)
  const load = () => api.vouchers().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 1 }}>Mã giảm giá / Voucher</h1><button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Tạo voucher</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Mã</th><th>Tên</th><th>Hội viên</th><th className="right">Điểm còn/Tổng</th><th className="right">Lượt còn</th><th>Hạn dùng</th><th>Trạng thái</th></tr></thead>
          <tbody>{rows.map(v => (
            <tr key={v.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(v.id)}>
              <td style={{ fontFamily: 'monospace' }}>{v.code}</td><td>{v.name}</td><td>{v.memberNo || '—'}</td>
              <td className="right">{fmtMoney(v.pointRemain)}/{fmtMoney(v.pointTotal)}</td><td className="right">{v.qtyUseRemain}/{v.qtyUseLimit}</td>
              <td>{fmtDate(v.expireDate)}</td><td><Badge text={v.statusText} css={v.statusCss} />{v.usable && <span className="badge success" style={{ marginLeft: 4 }}>DÙNG ĐƯỢC</span>}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={7} className="muted" style={{ padding: 20 }}>Chưa có voucher.</td></tr>}</tbody></table>
      </div>
      {open && <VoucherDetail id={open} onClose={() => setOpen(null)} onChanged={load} />}
      {show && <VoucherForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function VoucherDetail({ id, onClose, onChanged }) {
  const [v, setV] = useState(null); const [msg, setMsg] = useState(null); const [reds, setReds] = useState([])
  const load = () => { api.voucher(id).then(r => setV(r.data)); api.voucherRedemptions(id).then(r => setReds(r.data)) }
  useEffect(() => { load() }, [id])
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 3000) }
  const toggle = async () => { try { await api.setVoucherActive(id, !v.active); flash(true, v.active ? 'Đã tạm dừng.' : 'Đã kích hoạt.'); load(); onChanged() } catch (e) { flash(false, e.message) } }
  if (!v) return <Modal title="…" onClose={onClose}><p className="muted">Đang tải…</p></Modal>
  return (
    <Modal title={`${v.name} (${v.code})`} onClose={onClose} wide>
      <Flash msg={msg} />
      <div className="row" style={{ marginBottom: 8 }}><Badge text={v.statusText} css={v.statusCss} />{v.usable && <Badge text="DÙNG ĐƯỢC" css="success" />}
        <span className="pill" style={{ flex: 'none' }}>{v.redemptions} lượt dùng · {fmtMoney(v.pointUsed)} đã trừ</span></div>
      <div className="grid kpis" style={{ marginBottom: 12 }}>
        <div className="kpi"><div className="v" style={{ fontSize: 18 }}>{fmtMoney(v.pointRemain)}</div><div className="l">Điểm còn lại</div></div>
        <div className="kpi"><div className="v" style={{ fontSize: 18 }}>{fmtMoney(v.pointTotal)}</div><div className="l">Tổng điểm</div></div>
        <div className="kpi"><div className="v" style={{ fontSize: 18 }}>{fmtMoney(v.pointLimit)}</div><div className="l">Hạn mức/lần</div></div>
        <div className="kpi"><div className="v" style={{ fontSize: 18 }}>{v.qtyUseRemain}/{v.qtyUseLimit}</div><div className="l">Lượt còn/Tổng</div></div>
      </div>
      <div className="section-t">Lịch sử sử dụng</div>
      <table><thead><tr><th>Hội viên</th><th className="right">Điểm dùng</th><th className="right">Còn lại</th><th className="right">Lượt còn</th><th>Thời gian</th></tr></thead>
        <tbody>{reds.map(r => <tr key={r.id}><td>{r.memberNo || '—'}</td><td className="right">{fmtMoney(r.pointUsed)}</td><td className="right">{fmtMoney(r.pointRemainAfter)}</td><td className="right">{r.qtyUseRemainAfter}</td><td>{fmtDate(r.createdAt)}</td></tr>)}
          {reds.length === 0 && <tr><td colSpan={5} className="muted" style={{ padding: 16 }}>Chưa có lượt sử dụng.</td></tr>}</tbody></table>
      <div className="row" style={{ gap: 6, marginTop: 12 }}>
        <button className="btn sm" onClick={toggle}>{v.active ? 'Tạm dừng' : 'Kích hoạt'}</button>
      </div>
    </Modal>
  )
}

function VoucherForm({ onClose, onSaved }) {
  const [f, setF] = useState({ name: '', code: '', memberNo: '', pointTotal: 200000, pointLimit: 50000, qtyUseLimit: 4 }); const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { if (!f.name) { setErr('Cần tên'); return } await api.createVoucher({ ...f, pointTotal: Number(f.pointTotal), pointLimit: Number(f.pointLimit), qtyUseLimit: Number(f.qtyUseLimit) }); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Tạo voucher" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Tên *"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
        <Field label="Mã (trống = tự sinh)"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field></div>
      <Field label="Mã hội viên"><input value={f.memberNo} onChange={e => up('memberNo', e.target.value)} /></Field>
      <div className="row"><Field label="Tổng điểm"><input type="number" value={f.pointTotal} onChange={e => up('pointTotal', e.target.value)} /></Field>
        <Field label="Hạn mức mỗi lần"><input type="number" value={f.pointLimit} onChange={e => up('pointLimit', e.target.value)} /></Field>
        <Field label="Số lượt dùng"><input type="number" value={f.qtyUseLimit} onChange={e => up('qtyUseLimit', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Tạo voucher</button></div>
    </Modal>
  )
}

function Redeem() {
  const [f, setF] = useState({ code: '', amount: '', memberNo: '' }); const [res, setRes] = useState(null); const [err, setErr] = useState(null)
  const up = (k, v) => setF({ ...f, [k]: v })
  const doRedeem = async () => { try { const r = await api.redeem({ code: f.code, amount: f.amount === '' ? null : Number(f.amount), memberNo: f.memberNo }); setRes(r.data); setErr(null) } catch (e) { setErr(e.message); setRes(null) } }
  return (
    <>
      <h1>Dùng voucher (mô phỏng khách hàng)</h1>
      <div className="card">
        <div className="row"><Field label="Mã voucher"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field>
          <Field label="Số tiền cần trừ (trống = dùng hạn mức)"><input type="number" value={f.amount} onChange={e => up('amount', e.target.value)} /></Field></div>
        <Field label="Mã hội viên"><input value={f.memberNo} onChange={e => up('memberNo', e.target.value)} /></Field>
        <div style={{ marginTop: 12 }}><button className="btn" onClick={doRedeem}>💳 Dùng voucher</button></div>
      </div>
      {err && <Flash msg={{ ok: false, text: err }} />}
      {res && (
        <div className="card" style={{ borderLeft: `5px solid ${res.ok ? 'var(--success)' : 'var(--muted)'}`, textAlign: 'center' }}>
          <h2 style={{ color: res.ok ? 'var(--success)' : 'var(--ink)' }}>{res.ok ? '✅ Đã dùng voucher' : '⚠️ Không dùng được'}</h2>
          {res.ok && <p>Đã trừ: {fmtMoney(res.pointUsed)} · Còn lại: {fmtMoney(res.pointRemain)} · Lượt còn: {res.qtyUseRemain}</p>}
          <p className="muted">{res.msg}</p>
        </div>
      )}
    </>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="campaigns" element={<Campaigns />} />
        <Route path="entries" element={<Entries />} />
        <Route path="vouchers" element={<Vouchers />} />
        <Route path="play" element={<Play />} />
        <Route path="redeem" element={<Redeem />} />
      </Route>
    </Routes>
  )
}
