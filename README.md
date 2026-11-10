# ReadRS232

Ứng dụng Windows đọc dữ liệu từ thiết bị RS232 (cân, máy đo, máy quét…) hoặc TCP, xử lý giá trị rồi chuyển tiếp linh hoạt tới nhiều nơi cùng lúc: điền vào ô nhập của app khác, gõ phím, chuyển COM → COM (dùng với com0com), TCP, Clipboard, file CSV, HTTP.

Màn hình chính tối giản: chọn cổng, xem giá trị, bấm **Đọc**. Mọi tuỳ chọn nằm trong ⚙ Cài đặt.

## Tính năng

- **Nhiều tuyến**: mỗi tuyến = 1 nguồn → xử lý → nhiều đầu ra.
- **Nguồn**: cổng COM (đủ thông số, DTR/RTS, tự kết nối lại khi rút/cắm cáp), TCP client, TCP server.
- **Xử lý**: tách khung (theo dòng / ký tự / độ dài / khoảng lặng), Regex hoặc lấy số, nhân/cộng, làm tròn, dấu phẩy thập phân, min/max, chờ ổn định, bỏ trùng. Có ô thử dữ liệu mẫu xem kết quả ngay.
- **Đầu ra**: điền vào app khác (UI Automation / WM_SETTEXT / gõ phím, có **🎯 chọn ô bằng chuột**), gõ phím như máy quét mã vạch, chuyển tiếp COM/TCP thô hai chiều, TCP server, Clipboard, file CSV, HTTP.
- **Mẫu dữ liệu**: `{value}`, `{value:0.00}`, `{frame}`, `{hex}`, `{time:HH:mm:ss}`, `{route}`, `{n}`, hỗ trợ `\r \n \t \xHH`.
- **Monitor**: xem RX/TX dạng chữ hoặc HEX, gửi lệnh thử, giả lập thiết bị gửi.
- Thu xuống khay, luôn trên cùng, khởi động cùng Windows, chỉ 1 bản chạy; `--config "file.xml"` để chạy nhiều bộ cấu hình song song.

Cấu hình mặc định: COM 9600 8N1, lấy số đứng trước đơn vị (`12.50 kg` → `12.50`), **+13**, điền vào ô `EDIT.app` đầu tiên của `WorkSupportSystem` rồi nhấn Enter.

## Build

- .NET Framework 4.7.2, WinForms.
- Mở `ReadRS232/ReadRS232.sln` bằng Visual Studio, hoặc:

```bat
MSBuild ReadRS232\ReadRS232\ReadRS232.csproj -p:Configuration=Release
```

Repo không chứa khoá ký ClickOnce (`*.pfx`). Muốn publish ClickOnce thì tạo khoá mới trong Project → Properties → Signing, hoặc tắt "Sign the ClickOnce manifests".

## Cấu trúc

```
ReadRS232/ReadRS232/
  Core/   Config, Endpoints (COM/TCP), Processing, Outputs, Engine, WindowTarget, Native
  UI/     MainForm, RouteCard, MonitorPanel, SettingsForm, PickerOverlay, Theme
```

Cấu hình lưu ở `%AppData%\ReadRS232\config.xml`, lỗi ghi ở `%AppData%\ReadRS232\errors.log`.
