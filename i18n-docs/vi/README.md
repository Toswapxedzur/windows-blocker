# Windows Vault

Windows Vault là thành viên Windows gốc của dòng sản phẩm Vault. Đây là một ứng dụng .NET 8 WPF với trình soạn thảo WebView2, kho ứng dụng gốc, công cụ thực thi, thời gian chạy quy tắc tùy chỉnh và trung tâm cầu nối ứng dụng web cục bộ.

Mã là hợp đồng sản phẩm. Hướng dẫn sử dụng trong ứng dụng được duy trì là [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Khả năng hiện tại

- Nhóm mặc định cho các ứng dụng Windows đã chọn và Nhóm tùy chỉnh cho các quy tắc chính sách nâng cao.
- Chế độ ngay lập tức, trợ cấp và đếm ngược; lịch trình; đông cứng; giấc ngủ ngắn; và nhập/xuất nhóm.
- Kho ứng dụng Windows và các thành phần thực thi dựa trên cửa sổ.
- Trình chỉnh sửa WebView2 được lưu trữ từ `src/WindowsBlocker/WebAssets/`.
- Kiểm soát việc thực thi quy tắc tùy chỉnh bằng cách kiểm tra cú pháp và nguồn cấp dữ liệu nhật ký.
- Một trung tâm cầu nối loopback cho các nhóm tương thích được liên kết rõ ràng.
- Hẹn giờ gốc, bánh mì nướng và cửa sổ lớp phủ bảng điều khiển.

## Xây dựng

Sử dụng giải pháp và dự án đã đăng ký:

```powershell
dotnet build WindowsBlocker.sln
```

Dự án ứng dụng nhắm mục tiêu `net8.0-windows` và sử dụng WPF cộng với WebView2. Xây dựng và chạy nó trên Windows với thời gian chạy .NET SDK và WebView2 bắt buộc có sẵn.

##Bản đồ dự án

| Khu vực | Thư mục nguồn |
| --- | --- |
| Mô hình nhóm và đánh giá chính sách | `src/WindowsBlocker/Core/` |
| Thực thi bản địa | `src/WindowsBlocker/Enforcement/` |
| Khoảng không quảng cáo ứng dụng và cầu nối WebView | `src/WindowsBlocker/WebUI/` |
| Thời gian chạy quy tắc tùy chỉnh | `src/WindowsBlocker/Rules/` |
| Trung tâm cầu | `src/WindowsBlocker/Bridge/` |
| Cửa sổ và lớp phủ WPF | `src/WindowsBlocker/` |

## Tài liệu và bản dịch

Tài liệu tiếng Anh vẫn còn kinh điển. Nhãn giao diện người dùng sử dụng danh mục JSON hoàn chỉnh trong `src/WindowsBlocker/WebAssets/translation/`; các hướng dẫn đã dịch nằm bên cạnh `manual/en.md` và bản dịch của các tài liệu được duy trì còn lại nằm ở `i18n-docs/<locale>/`.
