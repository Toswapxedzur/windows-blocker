# Hướng dẫn sử dụng Windows Vault

Windows Vault có ba trang: **Vault** chặn ứng dụng native, **Bộ phân loại** gắn thẻ nội dung trình duyệt được hỗ trợ và **Activity** hiển thị mức sử dụng đã ghi nhận. Tiện ích trình duyệt thu thập nội dung được hỗ trợ và áp dụng việc chặn trong trình duyệt. Cài đặt và kết nối tiện ích trong trình duyệt bạn sử dụng.

## Bắt đầu nhanh

1. Trong **Vault**, thêm nhóm chặn và mục tiêu Apps, sau đó chọn ứng dụng bằng bộ chọn +.
2. Chọn hành vi chặn của nhóm và bật nhóm.
3. Trong **Bộ phân loại**, tạo nhóm, chọn nền tảng rồi thêm thẻ kèm mô tả.
4. Chọn cấp mô hình cục bộ và tải xuống nếu cần. Bật gắn thẻ trong cài đặt bộ phân loại và tiếp tục nhóm.
5. Mở nội dung được hỗ trợ trong trình duyệt đã kết nối. Cấu hình bộ lọc thẻ trong nhóm chặn của trình duyệt nếu muốn thẻ điều khiển việc chặn.

## Nhóm chặn

**Nhóm chặn** áp dụng chính sách chặn. **Nhóm phân loại** gắn thẻ cho nội dung; bản thân nhóm này không chặn gì.

1. Thêm một nhóm chặn và đặt tên.
2. Chọn mục tiêu trong **Áp dụng cho**.
3. Chọn thời điểm áp dụng chặn, rồi đặt lịch hoặc thời lượng cho phép nếu cần.
4. Bật nhóm. Các mục tiêu của nhóm dùng chung chính sách đó.

Các chỉnh sửa thông thường được lưu tự động. Nếu có lỗi, chỉnh sửa chưa được chấp nhận; hãy sửa trường đó rồi thử lại. Tắt nhóm để ngừng chính sách nhưng giữ cấu hình. **Xóa nhóm** sẽ xóa nhóm. Kéo nhóm để sắp xếp lại. Nhiều nhóm có thể áp dụng cho cùng mục tiêu; hoãn một nhóm không gỡ chặn của nhóm khác.

**Xuất** sao chép cấu hình nhóm. **Nhập** thay thế cấu hình của nhóm đã chọn sau khi xác nhận.

### Thời lượng cho phép và lịch

**Chặn ngay** áp dụng bất cứ khi nào nhóm đang bật khớp và lịch của nhóm có hiệu lực. **Chặn khi hết thời lượng cho phép** cho phép sử dụng nội dung khớp cho đến khi thời lượng cho phép cạn.

Đặt thời lượng cho phép theo phút và chu kỳ đặt lại theo giờ. Giới hạn trượt tính mức sử dụng trong khoảng thời gian ngay trước đó. Đặt lại lúc nửa đêm sẽ bắt đầu chu kỳ mới vào nửa đêm theo giờ địa phương, kể cả với giới hạn trượt.

Chọn các ngày trong tuần cần hoạt động và các khung giờ địa phương tùy chọn, mỗi dòng một khung, chẳng hạn **09:00-12:00**. Nếu không có khung giờ, lịch áp dụng suốt các ngày đã chọn. Giờ kết thúc phải muộn hơn giờ bắt đầu trong cùng ngày; hãy tách lịch qua đêm thành các ngày riêng.

### Hoãn

Cấu hình hoãn trong từng nhóm chặn. **Tạm dừng chặn** đình chỉ chính sách của nhóm trong thời lượng tạm dừng. **Cộng vào thời lượng cho phép** thêm phút sử dụng được cho nhóm giới hạn thời gian. Chỉ phần thời lượng cộng thêm đã dùng mới được tính là thời gian hoãn. Thời lượng cộng thêm chưa dùng sẽ hết hạn vào lần đặt lại kế tiếp; với giới hạn trượt, hết hạn sau một cửa sổ hoặc sớm hơn vào nửa đêm nếu bật tùy chọn đó.

**Độ trễ kích hoạt** hoãn yêu cầu hoãn trong khi việc chặn vẫn tiếp tục. **Thời gian chờ** là khoảng chờ sau khi hết hoãn trước lần yêu cầu tiếp theo. **Số lần xác nhận bắt buộc** đặt số bước xác nhận. Chỉ có thể hoãn nhóm đã khóa nếu cho phép trước khi khóa.

### Khóa và PIN

**Khóa** ngăn chỉnh sửa thông thường. Mở khóa cần mười lần xác nhận, cách nhau năm giây, cùng thời gian chờ đã đặt và PIN sáu chữ số. **Chờ trước khi mở khóa** nhận giá trị 0–72 giờ; 0 nghĩa là không chờ thêm.

Khi đang khóa, có thể kéo dài thời gian chờ và thêm PIN nếu chưa có. Không thể nới lỏng các điều kiện đó cho đến khi mở khóa nhóm. Xóa nhóm cũng phải tuân thủ thời gian chờ còn lại và PIN.

### Nhóm được liên kết

Dùng **Liên kết** để nối các nhóm được chọn rõ ràng trong những chương trình Vault khác. Các nhóm được liên kết dùng chung tên, cài đặt chính sách được hỗ trợ, mục tiêu, mức sử dụng và điều kiện khóa. Mỗi chương trình chỉnh sửa và thực thi các loại mục tiêu mà chương trình đó hỗ trợ; các mục tiêu loại khác vẫn có sẵn cho chương trình liên kết. Hủy liên kết vẫn giữ từng nhóm và cài đặt của nhóm.

Nếu thành viên được liên kết ngoại tuyến, có thể không chỉnh sửa được. Mở Windows Vault và trình duyệt đã liên kết để kết nối lại. Chính sách đã lưu cục bộ có thể tiếp tục áp dụng khi thành viên ngoại tuyến.

## Trợ giúp

Nhấp vào chữ **i** nhỏ bên cạnh trường để xem giải thích. Nhấp bên ngoài hoặc nhấn Escape để đóng. Danh sách nằm trong các hộp có thể cuộn; cuộn trong hộp để xem thêm mục. Tìm kiếm lọc danh sách đang hiển thị mà không xóa mục nào.

Quy tắc tùy chỉnh có [Hướng dẫn mã](../code-manual/vi.md) riêng. Hướng dẫn giải thích trình chỉnh sửa, kích hoạt, nhật ký, truy cập tệp và API được hỗ trợ.

## Ứng dụng native

Dùng bộ chọn + của mục tiêu Apps để chọn ứng dụng đã cài đặt. **Chặn mọi ứng dụng ngoại trừ các ứng dụng này** biến danh sách thành allowlist. Ứng dụng hệ thống, trình duyệt và chính Vault không bị chặn ở cấp native.

Ứng dụng bị chặn sẽ được yêu cầu thoát. **Yêu cầu ứng dụng bị chặn thoát lại sau mỗi (phút)** điều khiển việc thử lại. Chuyển hướng trang web, tạm dừng trang và ẩn feed do tiện ích trình duyệt thực thi; chúng không trở thành thao tác native của ứng dụng.

## Gắn thẻ nội dung

Mỗi nhóm gắn thẻ nội dung từ nền tảng được gán bằng cây thẻ và cài đặt mô hình riêng. Mỗi nền tảng thuộc một nhóm; chọn khi tạo vì sau đó không đổi được. Lịch và bộ lọc nhóm chặn không điều khiển tagging. Bật tagging trong Cài đặt và dùng **Tạm dừng gắn thẻ / Tiếp tục gắn thẻ** riêng từng nhóm. Tắt ghi feed nền tảng tại **Hoạt động → Ghi dữ liệu** cũng dừng tagging của feed đó.

### Thẻ và cài đặt mô hình

Tạo thẻ, mô tả ý nghĩa, đặt hoặc bỏ thẻ cha trong cây. Kéo thẻ để di chuyển nhánh. Mô tả rõ giúp mô hình phân biệt các thẻ giống nhau. Cài đặt mỗi nhóm độc lập. **Độ tin cậy tối thiểu của thẻ** đặt mức bằng chứng cần cho từng thẻ dự đoán từ 1 đến 5; nhóm mới bắt đầu ở 4 (hỗ trợ mạnh). Không có số lượng thẻ mục tiêu; mục có thể không có thẻ. **Hướng dẫn gắn thẻ** lưu tùy chọn của nhóm. Sửa đổi thông thường tự lưu; mỗi nhóm dùng connection, model và reasoning effort đã chọn rõ ràng.

### Kết nối AI

Cài đặt có ChatGPT subscription, OpenAI API, Gemini API và Grok API. Đăng ký ChatGPT dùng đăng nhập riêng của Vault và gói đủ điều kiện. API key chịu phí và giới hạn của nhà cung cấp đã chọn. Chọn nhà cung cấp rồi nhập key ngay dưới danh sách chung; chỉnh sửa key tự lưu, xóa nội dung trường sẽ xóa key. **Tải danh sách mô hình** lấy mô hình nhưng không tự chọn; hãy chọn model trong nhóm Gắn thẻ nội dung hoặc custom-rule.

**Mức độ suy luận** điều chỉnh mức suy nghĩ của mô hình: Model default, Low, Medium hoặc High nếu được hỗ trợ. Lựa chọn mới bắt đầu ở Medium; lựa chọn cũ giữ nguyên. Mức cao hơn có thể tốn thời gian và token. Web search luôn bật cho tagging; mô hình quyết định lúc dùng. Chọn mô hình hỗ trợ tìm kiếm và tagging có cấu trúc. Yêu cầu lỗi/không khả dụng vẫn giữ kết quả cache hợp lệ; mục mới chưa có kết quả cho đến khi chọn **Thử lại phân loại trên đám mây**. Vault không tự đổi nhà cung cấp; mỗi connection có chu kỳ retry và giới hạn lần thử riêng. Sửa thẻ trong tiện ích trình duyệt: nhấn **+ tag**, tìm và chọn để thêm; dùng nút xóa hoặc chọn thẻ rồi nhấn Delete một lần để xóa. **Chưa gắn thẻ** nghĩa là tagging xong nhưng không có thẻ; **Đang gắn thẻ** nghĩa là kết quả đang chờ. Sửa đổi của bạn có hiệu lực cho mục đó.


## Hoạt động

Hoạt động ghi cục bộ việc dùng ứng dụng đã bật, lượt truy cập trang web và **Nội dung đã xem** được hỗ trợ. Biểu đồ phản ánh dữ liệu đã ghi; khoảng trống không chứng minh PC không hoạt động. Chọn khoảng ngày. **Dòng thời gian** hiển thị theo giờ trong ngày; **Tổng** cộng thời lượng; **Khoảng thời gian** gom hoạt động mỗi khoảng thành khối dọc. **Màu sắc** là chú giải có thể nhấp; chọn nguồn để tập trung biểu đồ. Chọn ngày để xem từ ngày đó.

### Nhóm Hoạt động

Tạo nhóm để hiển thị chung ứng dụng và trang web đã chọn trong Usage. **Merge** dùng chung một tên và màu cho các thành viên trên Hoạt động. Nhóm Hoạt động sắp xếp hoạt động đã ghi và tách biệt với nhóm chặn và Gắn thẻ nội dung. Lưu trình chỉnh sửa bằng **Save**.

### Ghi và lưu giữ

Trong **Ghi dữ liệu**, bật/tắt ghi theo danh mục hoặc nguồn riêng. **Lưu giữ** đặt thời gian lưu lịch sử; **Mãi mãi** không tự hết hạn. Lựa chọn riêng có thể theo cài đặt rộng hơn. Tắt ghi sẽ dừng bản ghi mới; xóa lịch sử xóa mục đã ghi. Feed nền tảng thu thập nội dung hiển thị trên trang được hỗ trợ dù trang đã mở hay chưa. Feed **Hỗ trợ gắn thẻ** có thể cung cấp nội dung cho Gắn thẻ nội dung khi đang ghi. Thời hạn lưu feed tách biệt khỏi việc dùng ứng dụng và trang web. Tạm dừng nhóm tagging không tự dừng ghi.

## Cài đặt

Chọn ngôn ngữ giao diện trong Cài đặt. Nút Info cung cấp giải thích trường bằng ngôn ngữ đã chọn.

## Lưu và khắc phục sự cố

Các chỉnh sửa thông thường trong Vault và bộ phân loại được lưu tự động. Chỉnh sửa nhóm Activity cần **Lưu**. Thêm, xóa, tải mô hình, thử kết nối và lấy danh sách mô hình vẫn là thao tác chủ động.

Nếu thiếu thẻ, kiểm tra kết nối trình duyệt, công tắc gắn thẻ toàn cục, trạng thái tạm dừng nhóm, ghi nhận nền tảng và tải mô hình. Nếu nghiên cứu không chạy, kiểm tra đồng ý, lựa chọn nhóm, thông tin xác thực nhà cung cấp, mô hình nghiên cứu và trạng thái nghiên cứu. Nếu không chỉnh sửa được nhóm liên kết, kết nối lại các chương trình hoặc mở khóa như được hướng dẫn.
