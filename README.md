# SafetyGuard.WinForms

Ứng dụng **giám sát an toàn lao động bằng AI** được xây dựng bằng **C# WinForms trên .NET 8**, hỗ trợ phát hiện vi phạm PPE từ camera RTSP hoặc file ảnh/video offline. Project sử dụng 2 model ONNX theo pipeline mới:

- `YOLOv11_ppe.onnx`: phát hiện người và trang bị bảo hộ lao động.
- `YOLOv11_smoke.onnx`: phát hiện hành vi hút thuốc.

Ứng dụng lưu cấu hình, lịch sử vi phạm và bằng chứng vào SQLite/local storage, đồng thời cung cấp giao diện dashboard, giám sát real-time, phân tích offline, quản lý lịch sử và cấu hình hệ thống.

---

## Mục lục

- [Tính năng chính](#tính-năng-chính)
- [Công nghệ sử dụng](#công-nghệ-sử-dụng)
- [Kiến trúc tổng quan](#kiến-trúc-tổng-quan)
- [Luồng xử lý AI](#luồng-xử-lý-ai)
- [Cấu trúc thư mục](#cấu-trúc-thư-mục)
- [Yêu cầu hệ thống](#yêu-cầu-hệ-thống)
- [Cài đặt và chạy project](#cài-đặt-và-chạy-project)
- [Cấu hình ứng dụng](#cấu-hình-ứng-dụng)
- [Dữ liệu lưu trữ cục bộ](#dữ-liệu-lưu-trữ-cục-bộ)
- [Model ONNX và mapping nhãn](#model-onnx-và-mapping-nhãn)
- [Build / Publish](#build--publish)
- [Troubleshooting](#troubleshooting)
- [Hướng phát triển tiếp theo](#hướng-phát-triển-tiếp-theo)

---

## Tính năng chính

### 1. Dashboard

- Hiển thị tổng số vi phạm trong ngày.
- Thống kê số vi phạm mức nghiêm trọng.
- Tính chỉ số compliance rate dạng demo.
- Hiển thị số lượng camera đã cấu hình.
- Biểu đồ xu hướng vi phạm theo 7 ngày hoặc 30 ngày.
- Biểu đồ tỷ lệ vi phạm theo loại.

### 2. Real-time Monitor

- Kết nối camera qua RTSP.
- Hỗ trợ chế độ xem **Single Camera** và **2×2 Grid**.
- Start/Stop detection trực tiếp từ giao diện.
- Hiển thị trạng thái camera: `CONNECTED`, `RECONNECTING`, `OFFLINE`.
- Render bounding box theo `TrackId`.
- Hiển thị trạng thái PPE trên từng người:
  - Helmet
  - Vest
  - Gloves
  - Glasses
  - Boots
  - Smoking
- Tự động tạo sự kiện vi phạm và lưu bằng chứng khi phát hiện lỗi.

### 3. Offline Analysis

- Phân tích ảnh hoặc video offline.
- Hỗ trợ ảnh:
  - `.jpg`
  - `.jpeg`
  - `.png`
  - `.bmp`
- Hỗ trợ video:
  - `.mp4`
  - `.avi`
  - `.mkv`
  - `.mov`
- Preview frame đang phân tích.
- Lưu các vi phạm phát hiện được vào cùng hệ thống lịch sử.

### 4. History & Evidence

- Xem danh sách vi phạm đã ghi nhận.
- Lọc theo:
  - Từ khóa camera/ghi chú
  - Loại vi phạm
  - Trạng thái xử lý
  - Khoảng thời gian
- Xem ảnh bằng chứng của từng vi phạm.
- Cập nhật trạng thái xử lý:
  - `New`
  - `Acknowledged`
  - `Resolved`
  - `FalseAlarm`
- Xuất dữ liệu:
  - CSV
  - Excel `.xlsx`, có nhúng ảnh snapshot nếu tồn tại
- Xóa dữ liệu theo:
  - Dòng được chọn
  - Kết quả đang lọc
  - Toàn bộ lịch sử

### 5. System Settings

- Quản lý danh sách camera RTSP.
- Thêm/sửa/xóa camera.
- Bật/tắt từng camera.
- Test nhanh kết nối RTSP.
- Quản lý detection rules theo loại vi phạm.
- Bật/tắt rule, chỉnh sensitivity/threshold, chọn mức độ cảnh báo.
- Cấu hình anti-spam:
  - Cooldown giữa các cảnh báo trùng lặp.
  - Số frame liên tiếp tối thiểu trước khi ghi nhận vi phạm.
- Cấu hình lưu trữ bằng chứng:
  - Đường dẫn lưu ảnh bằng chứng.
  - Thời gian giữ dữ liệu 7/30/90 ngày.
  - Bật/tắt lưu snapshot.
  - Tùy chọn lưu short clip hiện đang là placeholder.

---

## Công nghệ sử dụng

| Thành phần | Công nghệ |
|---|---|
| Desktop UI | C# WinForms, .NET 8 |
| UI Components | Guna.UI2.WinForms |
| AI Inference | Microsoft.ML.OnnxRuntime.Gpu |
| Computer Vision | OpenCvSharp4 |
| Database | SQLite |
| Data Access | Dapper |
| Chart | LiveChartsCore + SkiaSharp |
| Export Excel | ClosedXML |
| Model format | ONNX |

---

## Kiến trúc tổng quan

Ứng dụng được khởi tạo từ `Program.cs`, sau đó `AppBootstrap` tạo và liên kết các service chính:

```text
Program.cs
   ↓
AppBootstrap.Build()
   ├── AppPaths
   ├── LogService
   ├── SqliteDb
   ├── DbInitializer
   ├── SqliteAppSettingsService
   ├── SqliteViolationRepository
   ├── EvidenceService
   ├── ExportService
   ├── DualOnnxDetector
   ├── ViolationEngine
   └── OfflineAnalyzer
   ↓
MainForm
   ├── DashboardPage
   ├── RealtimePage
   ├── HistoryPage
   ├── OfflinePage
   └── SettingsPage
```

Các phần chính:

- `Vision/`: đọc frame, chạy ONNX, tracking người và phân tích offline.
- `Services/`: SQLite, logging, evidence, export, xử lý logic vi phạm.
- `Pages/`: giao diện các màn hình chính.
- `Controls/`: control camera real-time và badge trạng thái.
- `Models/`: model dữ liệu cấu hình, detection, violation và enum.

---

## Luồng xử lý AI

### Real-time pipeline

```text
RTSP Camera
   ↓
RtspFrameSource
   ↓
FramePacket
   ↓
CameraViewControl
   ↓
Detect mỗi N frame
   ↓
DualOnnxDetector
   ├── YOLOv11_ppe.onnx
   └── YOLOv11_smoke.onnx
   ↓
SortTracker
   ↓
PpeMapper
   ↓
ViolationEngine
   ↓
SQLite + Evidence Snapshot + Live Event UI
```

Giải thích:

1. `RtspFrameSource` đọc frame từ camera RTSP bằng OpenCV.
2. `CameraViewControl` nhận frame và giới hạn tốc độ cập nhật UI để tránh lag.
3. AI không chạy trên mọi frame. Project dùng `DetectEveryNFrames` để giảm tải.
4. `DualOnnxDetector` chạy 2 model ONNX:
   - PPE model cho người và trang bị bảo hộ.
   - Smoke model cho hút thuốc.
5. `SortTracker` gán `TrackId` cho từng người.
6. `PpeMapper` gán helmet/vest/gloves/glasses/boots/smoking vào từng người dựa trên bbox.
7. `ViolationEngine` kiểm tra rule, thời gian tồn tại, số frame liên tiếp và cooldown.
8. Khi có vi phạm, hệ thống lưu record vào SQLite và lưu snapshot bằng chứng.

### Offline pipeline

```text
Image/Video File
   ↓
OfflineAnalyzer
   ↓
OpenCvSharp Decode
   ↓
DualOnnxDetector
   ↓
ViolationEngine
   ↓
SQLite + Evidence + Preview UI
```

Với video offline, project dùng worker riêng cho detection để preview không bị block hoàn toàn. Trong `OfflinePage`, video đang được cấu hình sample mỗi 4 frame:

```csharp
sampleEveryNFrames: 4
```

---

## Cấu trúc thư mục

```text
SafetyGuard.Winforms-new_pipeline/
├── SafetyGuard.WinForms.sln
├── SafetyGuard.WinForms/
│   ├── Assets/
│   │   └── Models/
│   │       ├── YOLOv11_ppe.onnx
│   │       └── YOLOv11_smoke.onnx
│   ├── Controls/
│   │   ├── BadgeLabel.cs
│   │   └── CameraViewControl.cs
│   ├── Dialogs/
│   │   ├── CameraEditDialog.cs
│   │   └── ViolationEvidenceDialog.cs
│   ├── Models/
│   │   ├── AppSettings.cs
│   │   ├── CameraConfig.cs
│   │   ├── DetectionResult.cs
│   │   ├── DetectionRule.cs
│   │   ├── Enums.cs
│   │   ├── PersonState.cs
│   │   └── ViolationRecord.cs
│   ├── Pages/
│   │   ├── DashboardPage.cs
│   │   ├── HistoryPage.cs
│   │   ├── OfflinePage.cs
│   │   ├── RealtimePage.cs
│   │   └── SettingsPage.cs
│   ├── Services/
│   │   ├── DbInitializer.cs
│   │   ├── EvidenceService.cs
│   │   ├── ExportService.cs
│   │   ├── SqliteAppSettingsService.cs
│   │   ├── SqliteViolationRepository.cs
│   │   └── ViolationEngine.cs
│   ├── UI/
│   │   ├── AppColors.cs
│   │   ├── ControlFactory.cs
│   │   └── UiHelpers.cs
│   ├── Vision/
│   │   ├── DualOnnxDetector.cs
│   │   ├── OfflineAnalyzer.cs
│   │   ├── RtspFrameSource.cs
│   │   └── SortTracker.cs
│   ├── AppBootstrap.cs
│   ├── MainForm.cs
│   ├── Program.cs
│   └── SafetyGuard.WinForms.csproj
├── .gitattributes
└── .gitignore
```

---

## Yêu cầu hệ thống

- Windows 10/11 x64.
- Visual Studio 2022, khuyến nghị cài workload:
  - `.NET desktop development`
- .NET 8 SDK.
- Camera RTSP hoặc file ảnh/video để test.
- GPU NVIDIA là tùy chọn.
  - Project dùng `Microsoft.ML.OnnxRuntime.Gpu` và sẽ thử bật CUDA Execution Provider.
  - Nếu CUDA không khả dụng, code hiện tại sẽ ghi log cảnh báo và fallback về CPU.

> Lưu ý: Project target `net8.0-windows10.0.19041.0`, vì vậy nên build/chạy trên Windows.

---

## Cài đặt và chạy project

### Cách 1: Chạy bằng Visual Studio

1. Clone project:

```bash
git clone <your-repository-url>
cd SafetyGuard.Winforms-new_pipeline
```

2. Mở file solution:

```text
SafetyGuard.WinForms.sln
```

3. Chọn cấu hình:

```text
Debug | x64
```

hoặc:

```text
Release | x64
```

4. Restore NuGet packages nếu Visual Studio chưa tự restore.

5. Nhấn `F5` hoặc `Ctrl + F5` để chạy.

### Cách 2: Chạy bằng terminal

Tại thư mục gốc project:

```bash
dotnet restore .\SafetyGuard.WinForms\SafetyGuard.WinForms.csproj
dotnet build .\SafetyGuard.WinForms\SafetyGuard.WinForms.csproj -c Release
dotnet run --project .\SafetyGuard.WinForms\SafetyGuard.WinForms.csproj
```

---

## Cấu hình ứng dụng

### Thêm camera RTSP

Vào màn hình **System Settings**:

1. Chọn **Add Camera**.
2. Nhập tên camera.
3. Nhập RTSP URL.
4. Bật `Enabled`.
5. Lưu lại.
6. Có thể dùng nút test để kiểm tra camera online/offline.

Ví dụ RTSP URL:

```text
rtsp://username:password@192.168.1.100:554/stream1
```

### Chạy giám sát real-time

1. Vào **Real-time Monitor**.
2. Chọn chế độ `Single` hoặc `2×2 Grid`.
3. Nhấn **Start Detection**.
4. Khi có vi phạm, event sẽ xuất hiện ở panel **Live Events** và được lưu vào lịch sử.

### Phân tích ảnh/video offline

1. Vào **Offline Analysis**.
2. Chọn file ảnh hoặc video.
3. Nhấn **Run**.
4. Xem preview và danh sách event được phát hiện.
5. Các vi phạm offline được lưu với camera name `Offline Import`.

### Quản lý lịch sử vi phạm

Vào **History & Evidence** để:

- Lọc vi phạm.
- Xem snapshot evidence.
- Cập nhật trạng thái xử lý.
- Export CSV/Excel.
- Xóa dữ liệu không cần thiết.

---

## Dữ liệu lưu trữ cục bộ

Mặc định, ứng dụng lưu dữ liệu tại:

```text
%LOCALAPPDATA%\SafetyGuard
```

Bên trong có các thư mục/file chính:

```text
%LOCALAPPDATA%\SafetyGuard\data\safetyguard.db
%LOCALAPPDATA%\SafetyGuard\logs\app-yyyyMMdd.log
%LOCALAPPDATA%\SafetyGuard\evidence\yyyyMMdd\*.jpg
```

Ý nghĩa:

- `safetyguard.db`: SQLite database chứa camera, rules, settings và violations.
- `logs`: log ứng dụng theo ngày.
- `evidence`: ảnh snapshot bằng chứng.

Có thể đổi thư mục lưu evidence trong **System Settings → Evidence Path**.

---

## Database schema

Database được tạo tự động trong `DbInitializer.cs` với các bảng chính:

### `cameras`

Lưu danh sách camera RTSP.

| Cột | Ý nghĩa |
|---|---|
| `id` | ID camera |
| `name` | Tên camera |
| `rtsp_url` | Đường dẫn RTSP |
| `enabled` | Bật/tắt camera |

### `rules`

Lưu rule phát hiện vi phạm.

| Cột | Ý nghĩa |
|---|---|
| `type` | Enum `ViolationType` |
| `enabled` | Bật/tắt rule |
| `threshold` | Ngưỡng confidence |
| `level` | Mức độ vi phạm |

### `settings`

Lưu các cấu hình hệ thống dạng key/value.

### `violations`

Lưu lịch sử vi phạm.

| Cột | Ý nghĩa |
|---|---|
| `id` | ID vi phạm |
| `time_utc_ms` | Thời gian UTC dạng epoch milliseconds |
| `camera_id` | ID camera |
| `camera_name` | Tên camera |
| `type` | Loại vi phạm |
| `level` | Mức độ |
| `status` | Trạng thái xử lý |
| `confidence` | Độ tin cậy |
| `snapshot_path` | Đường dẫn ảnh bằng chứng |
| `clip_path` | Đường dẫn clip, hiện là placeholder |
| `notes` | Ghi chú |
| `track_id` | ID tracking người |
| `person_box` | Bbox của người vi phạm |

---

## Model ONNX và mapping nhãn

Model được đặt trong:

```text
SafetyGuard.WinForms\Assets\Models
```

Các file hiện tại:

```text
YOLOv11_ppe.onnx
YOLOv11_smoke.onnx
```

Trong file `.csproj`, 2 model này được cấu hình copy vào output directory:

```xml
<CopyToOutputDirectory>Always</CopyToOutputDirectory>
```

### PPE model mapping

Mapping class ID hiện nằm trong `Vision/DualOnnxDetector.cs`:

| Class ID | ObjectClass |
|---:|---|
| 0 | Boots |
| 1 | Glasses |
| 2 | Gloves |
| 3 | Helmet |
| 4 | NoBoots |
| 5 | NoGlasses |
| 6 | NoGloves |
| 7 | NoHelmet |
| 8 | NoVest |
| 9 | Person |
| 10 | Vest |

### Smoke model mapping

| Class ID | ObjectClass |
|---:|---|
| 0 | Smoking |

Nếu thay model khác, cần kiểm tra lại thứ tự label và sửa mapping trong `DualOnnxDetector.cs`.

---

## Các loại vi phạm hỗ trợ

Enum `ViolationType` hiện hỗ trợ:

```csharp
NoHelmet,
NoVest,
NoGloves,
NoGlasses,
NoBoots,
Smoking
```

Enum `ViolationLevel` gồm:

```csharp
Info,
Warning,
Critical
```

Enum `ViolationStatus` gồm:

```csharp
New,
Acknowledged,
Resolved,
FalseAlarm
```

---

## Build / Publish

### Build Release

```bash
dotnet build .\SafetyGuard.WinForms\SafetyGuard.WinForms.csproj -c Release
```

### Publish cho Windows x64

```bash
dotnet publish .\SafetyGuard.WinForms\SafetyGuard.WinForms.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=false ^
  -o .\publish
```

Sau khi publish, kiểm tra trong thư mục output có đủ:

```text
publish\SafetyGuard.WinForms.exe
publish\Assets\Models\YOLOv11_ppe.onnx
publish\Assets\Models\YOLOv11_smoke.onnx
```

Không nên bật single-file publish nếu chưa xử lý lại cách load model, vì code hiện tại load model theo đường dẫn:

```text
AppDomain.CurrentDomain.BaseDirectory\Assets\Models\*.onnx
```

---

## Troubleshooting

### 1. App báo thiếu model ONNX

Kiểm tra 2 file sau có tồn tại không:

```text
SafetyGuard.WinForms\Assets\Models\YOLOv11_ppe.onnx
SafetyGuard.WinForms\Assets\Models\YOLOv11_smoke.onnx
```

Nếu chạy bản publish, kiểm tra thư mục:

```text
publish\Assets\Models
```

### 2. CUDA không chạy hoặc log báo fallback CPU

Project đang thử bật CUDA Execution Provider. Nếu máy không có GPU NVIDIA, driver/CUDA/cuDNN không tương thích hoặc runtime thiếu thư viện, ứng dụng sẽ fallback CPU.

Cách xử lý:

- Vẫn có thể chạy bằng CPU, nhưng tốc độ inference sẽ chậm hơn.
- Nếu muốn chạy GPU, cần cài đúng driver NVIDIA và bộ CUDA/cuDNN tương thích với ONNX Runtime GPU đang dùng.
- Kiểm tra log tại:

```text
%LOCALAPPDATA%\SafetyGuard\logs
```

### 3. Camera RTSP bị `NO SIGNAL`

Kiểm tra:

- RTSP URL có đúng không.
- Camera và máy tính có cùng mạng không.
- Camera có yêu cầu username/password không.
- Stream có mở được bằng VLC không.
- Camera đã bật `Enabled` trong Settings chưa.

### 4. Build lỗi liên quan OpenCV native runtime

Kiểm tra:

- Đang build `x64`.
- Đã restore NuGet packages.
- Project có package `OpenCvSharp4.runtime.win`.

### 5. Muốn reset toàn bộ dữ liệu local

Đóng ứng dụng rồi xóa thư mục:

```text
%LOCALAPPDATA%\SafetyGuard
```

Khi chạy lại, app sẽ tự tạo database và thư mục cần thiết.

### 6. App chạy nặng hoặc FPS thấp

Có thể giảm tải bằng cách:

- Giảm số camera chạy cùng lúc.
- Giảm độ phân giải stream từ camera.
- Tăng khoảng cách detect frame trong code `DetectEveryNFrames`.
- Dùng GPU nếu có thể.
- Với video offline, tăng `sampleEveryNFrames` trong `OfflinePage.cs`.

---

## Ghi chú khi đưa lên GitHub

Nên commit:

```text
SafetyGuard.WinForms.sln
SafetyGuard.WinForms/**
.gitattributes
.gitignore
README.md
```

Không nên commit:

```text
bin/
obj/
.vs/
*.user
*.suo
*.db
logs/
evidence/
```

Hai model ONNX hiện khoảng hơn 10MB mỗi file, vẫn dưới giới hạn file thông thường của GitHub. Nếu sau này model lớn hơn, nên dùng Git LFS.

---

## Hướng phát triển tiếp theo

Một số hướng có thể nâng cấp:

- Thêm notification thực tế khi có vi phạm.
- Lưu short clip trước/sau thời điểm vi phạm thay vì placeholder.
- Thêm phân quyền người dùng.
- Thêm multi-camera dashboard chi tiết hơn.
- Cho phép chỉnh `DetectEveryNFrames`, `TrackIouThreshold`, `PpeIouThreshold` trực tiếp trên UI.
- Tối ưu tracking bằng thuật toán mạnh hơn.
- Tách inference thành service riêng nếu muốn mở rộng nhiều camera.
- Thêm unit test cho `ViolationEngine`, `PpeMapper`, `SortTracker`.
- Thêm CI build cho Windows.

---

## License

Project hiện chưa khai báo license. Nếu muốn public trên GitHub, nên bổ sung file `LICENSE`, ví dụ MIT License hoặc license phù hợp với yêu cầu của nhóm/dự án.
