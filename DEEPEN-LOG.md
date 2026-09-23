# DEEPEN-LOG

- 2026-09-23 — Port nghiệp vụ **mã giảm giá / điểm voucher** từ nguồn Loyalty (`Crd_MemberVoucher` + `Crd_MemberVoucherTransaction`). Thêm entity `Voucher`/`VoucherRedemption`, `VoucherService` (quy tắc: Active + chưa hết hạn + còn điểm + còn lượt; mỗi lần trừ tối đa `PointLimit`, giảm 1 lượt, chống vượt hạn mức), seed 2 voucher mẫu, API `/api/voucher/redeem` + `api/v1/vouchers*`, 6 test. Build Release 0 error, 12/12 test pass. Commit `ef13edf` đã push origin/main.
