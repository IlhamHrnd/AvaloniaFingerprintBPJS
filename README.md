# AvaloniaFingerprint

Aplikasi Avalonia (.NET 8, Windows) hasil ekstraksi tiga form fingerprint dari
project WinForms `KonfirmasiPendaftaranOnline`:

| Tab WinForms | Tab Avalonia |
|---|---|
| `DaftarFingerprint` | **Daftar Fingerprint** |
| `VerifikasiFingerprint` | **Verifikasi Fingerprint** |
| `RegistrasiFingerprint` | **Registrasi Fingerprint** |
| — | **Kode Booking** |
| — | **Verifikasi Wajah** (baru, masih placeholder) |

## Perilaku utama

* **Semua tab terkunci sebelum login.** `TabItem.IsEnabled` terikat ke
  `MainWindowViewModel.IsLoggedIn`, dan sebuah overlay menjelaskan cara membukanya.
* **Tombol Login di kanan atas** membuka dialog `LoginWindow`. Kredensial dikirim ke
  endpoint `Bpjs:LoginUrl`; token yang dikembalikan disimpan di `SessionService` dan
  dipakai sebagai header `token` pada panggilan verifikasi/registrasi. Tombol yang sama
  berubah menjadi **Logout** setelah login.
* **Deteksi USB fingerprint.** `UsbDeviceWatcher` menggabungkan dua sinyal: langganan WMI
  `Win32_DeviceChangeEvent` (reaksi seketika saat dicabut/dipasang) dan pemindaian ulang
  berkala sebagai jaring pengaman. Bila SDK DigitalPersona tersedia, daftar reader dari
  SDK dipakai lebih dulu; jika tidak, pencocokan dilakukan lewat `Win32_PnPEntity`
  (kelas `Biometric`, vendor id `VID_05BA`, atau kata kunci di `appsettings.json`).
  Status tampil di header dan tombol scan/verifikasi otomatis nonaktif saat terputus.
* **Deteksi kamera.** `CameraDeviceWatcher` memakai mekanisme yang sama dengan watcher USB:
  event WMI `Win32_DeviceChangeEvent` + pemindaian ulang berkala, dengan tipe event yang sama
  (`DeviceStatusEventArgs`). Kamera dipilih **murni lewat pencocokan `cameraKeywords`**:
  device dianggap kamera hanya bila namanya mengandung salah satu keyword, yaitu
  `Webcam` → `Facecam` → `Integrated Camera` → `USB Video Device` → `UVC` →
  `Windows Virtual Camera` (urutan array = prioritas). Pencocokan tidak case-sensitive dan
  substring, sehingga `Integrated Camera`, `Integrated Camera HD`, dan `Integrated CAMERA`
  sama-sama cocok.

  Keyword terakhir, `Windows Virtual Camera`, adalah jalur untuk kamera HP yang diekspos Windows
  lewat **Link to Windows / Phone Link**: Windows menampilkannya sebagai *software device*
  bernama `Windows Virtual Camera Device`. Karena pencocokan bersifat substring, keyword tersebut
  mengenai device itu tanpa kode khusus. Menambah/mengubah kamera cukup dengan mengedit daftar
  keyword — itulah satu-satunya titik ekstensi deteksi kamera.

  **Tidak ada fallback.** Bila tidak ada keyword yang cocok, kamera dianggap tidak ditemukan
  meskipun Windows mengenali device tersebut, dan halaman Verifikasi Wajah menampilkan
  "Kamera tidak ditemukan". Device dengan status error/tidak hadir juga tidak dipakai.

  Detail status berasal dari satu fungsi `CameraServices.ResolveDetail()`: saat terhubung,
  **nama device selalu ditampilkan** supaya bisa dipastikan kamera mana yang dikenali Windows.
  Halaman Verifikasi Wajah juga menampilkan daftar kamera hasil scan
  (`CameraServices.AvailableCameras`) untuk memudahkan pengecekan.
  Status tampil di header sebagai indikator kedua (di sebelah indikator fingerprint) dan
  menjadi sumber status yang sama untuk area kamera di halaman **Verifikasi Wajah**.
  Tahap ini hanya mendeteksi dan memilih device — stream, capture, dan pemrosesan wajah belum ada.
  Aplikasi **tidak** terhubung langsung ke HP (tanpa socket/IP camera/HTTP/Android API/Bluetooth):
  yang dibaca hanya device yang memang sudah diekspos Windows.

## Konfigurasi

`appsettings.json` (disalin ke folder output) menggantikan `App.config`:

```json
{
  "Bpjs":        { "LoginUrl": "...", "VerifyUrl": "...", "RegistUrl": "...", "TimeoutSeconds": 20 },
  "Fingerprint": { "CaptureTimeoutMs": 7000, "VerifyTimeoutMs": 5000, "FingersPerHand": 4,
                   "RightHandIndex": 1, "LeftHandIndex": 6 },
  "Device":      { "PollIntervalSeconds": 2, "MatchKeywords": [ "U.are.U", "DigitalPersona", ... ] },
  "Storage":     { "RegistrationsFile": "registrations.json" }
}
```

## SDK DigitalPersona

`DPUruNet.dll` tidak redistributable, jadi referensinya bersyarat — lihat
[lib/README.md](lib/README.md). Tanpa SDK aplikasi tetap jalan dan bisa diuji, hanya
pemindaian yang menampilkan pesan bahwa SDK belum tersedia.

## Build & jalankan

```bash
dotnet run --project AvaloniaFingerprint.csproj
```

## Catatan perbedaan dari versi WinForms

* `DaftarFingerprint` di WinForms masih form kosong. Di sini tab tersebut diisi daftar
  registrasi yang dikirim dari komputer ini (`registrations.json`), lengkap dengan
  pencarian dan pratinjau gambar sidik jari.
* Kredensial petugas tidak lagi di-hardcode di `LoginPetugas.cs`; diisi lewat dialog login.
* RestSharp diganti `HttpClient`, `System.Drawing` diganti SkiaSharp
  (lihat `Services/ImageCodec.cs`), dan `AutoClosingMessageBox` diganti pesan inline.
* Verifikasi tidak lagi menulis ke tabel `PatientBiometric` — kode itu sudah dikomentari
  di form aslinya dan membutuhkan EntitySpaces/.NET Framework.
