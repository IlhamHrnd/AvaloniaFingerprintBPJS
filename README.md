# AvaloniaFingerprint

Aplikasi Avalonia (.NET 8, Windows) hasil ekstraksi tiga form fingerprint dari
project WinForms `KonfirmasiPendaftaranOnline`:

| Tab WinForms | Tab Avalonia |
|---|---|
| `DaftarFingerprint` | **Daftar Fingerprint** |
| `VerifikasiFingerprint` | **Verifikasi Fingerprint** |
| `RegistrasiFingerprint` | **Registrasi Fingerprint** |
| — | **Kode Booking** |
| — | **Verifikasi Wajah** (live camera view + upload foto) |

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

  Detail status berasal dari satu fungsi `CameraServices.ResolveDetail()`: saat streaming,
  **nama device selalu ditampilkan** (beserta index OpenCV yang dipakai) supaya bisa dipastikan
  kamera mana yang aktif. Halaman Verifikasi Wajah juga menampilkan daftar kamera hasil scan
  (`CameraServices.AvailableCameras`) untuk memudahkan pengecekan.
  Status tampil di header sebagai indikator kedua (di sebelah indikator fingerprint) dan
  menjadi sumber status yang sama untuk area kamera di halaman **Verifikasi Wajah**.
  Aplikasi **tidak** terhubung langsung ke HP (tanpa socket/IP camera/HTTP/Android API/Bluetooth):
  yang dibaca hanya device yang memang sudah diekspos Windows.
* **Live camera view.** Deteksi device saja tidak cukup untuk menampilkan gambar, jadi stream
  dibuka oleh `CameraPreviewServices` memakai **OpenCvSharp4** (`VideoCapture`):

  * **Index tidak bisa dipetakan dari nama.** `VideoCapture` hanya menerima index (`int`), dan
    urutan index OpenCV tidak sama dengan urutan WMI. Karena itu index dicoba satu per satu
    (`0..N-1`, dengan `N` = jumlah kamera terdeteksi) dan percobaan berhenti pada index pertama
    yang benar-benar mengeluarkan frame. Untuk mesin dengan banyak kamera, index bisa dikunci
    lewat `Apps:CameraIndex` di `appsettings.json` (`null` = coba otomatis).
  * **Backend MSMF lebih dulu, lalu DSHOW.** Kamera virtual (Link to Windows / Phone Link)
    diekspos lewat Media Foundation, sehingga `MSMF` dicoba lebih dulu; `DSHOW` hanya cadangan.
  * **Terbuka berarti ada frame.** `VideoCapture.IsOpened` saja bisa `true` untuk device yang
    sudah hilang, jadi device baru dianggap terbuka setelah ada frame nyata yang keluar.
  * **Satu stream saja.** Start/stop diserialkan dengan `SemaphoreSlim`, dan start ditolak saat
    sudah streaming, sehingga menekan tombol berulang tidak pernah menghasilkan dua stream.
    Tombol "Hubungkan Kamera" selalu menjalankan urutan stop → lepas device → buka ulang → start.
  * **Frame BGRA ke `WriteableBitmap`, double-buffered.** OpenCV menghasilkan BGR sedangkan
    Avalonia merender BGRA8888, jadi setiap frame dikonversi lalu digambar ke `WriteableBitmap`.
    Dua bitmap dipakai bergantian karena Avalonia hanya menggambar ulang `Image` saat referensi
    `Source` berubah — menulis ke bitmap yang sama tidak akan terlihat bergerak. Pembacaan frame
    berjalan di thread background dan penggambaran di UI thread, dengan frame yang datang saat UI
    masih sibuk dibuang supaya tidak menumpuk (target ~20 fps).
  * **Siklus hidup.** Stream dibuka otomatis setelah login dan saat device terdeteksi, ditutup
    saat logout, dan dilepas saat aplikasi ditutup (`AppServices.Dispose()`).
  * **Indikator hijau = stream benar-benar terbuka.** Header dan halaman Verifikasi Wajah
    menurunkan status dari kondisi stream, bukan dari deteksi device: `Kamera terhubung`
    (stream berjalan), `Kamera gagal dibuka` (device ada tetapi stream gagal dibuka), dan
    `Kamera terputus` (device hilang / stream terputus).

  Face detection/recognition, encoding, dan pemanggilan API Frista **belum** ada — yang
  ditampilkan murni frame dari device yang sudah diekspos Windows.

* **Upload foto (sumber gambar manual).** Halaman Verifikasi Wajah bisa memakai foto dari file
  sebagai pengganti live camera, supaya verifikasi tetap bisa dilakukan saat kamera tidak
  tersedia:

  * **Dua sumber, satu area gambar.** `Upload Foto` membuka dialog pilih file
    (`IStorageProvider.OpenFilePickerAsync`) dengan filter `*.jpg`, `*.jpeg`, `*.png`. Ekstensi
    tetap divalidasi ulang setelah file dipilih, dan file dengan format lain ditolak dengan pesan,
    bukan exception. Foto yang dipilih menggantikan live view di area kamera, dan tombol
    penukar sumber (label mengikuti kondisi: `Ganti ke Live Camera` / `Tampilkan Foto Upload`)
    mengembalikannya. Selama foto tampil, frame kamera tidak digambar sama sekali — tidak ada CPU
    terbuang untuk gambar yang tidak terlihat.
  * **Foto terpisah dari stream.** Foto upload tetap tampil walaupun kamera terputus, karena
    sumber gambar disimpan terpisah dari status stream. Sebaliknya, hasil verifikasi juga tidak
    hilang saat sumber gambar ditukar.
  * **"Verifikasi Wajah" menangkap gambar saat tombol ditekan.** Untuk sumber live yang disalin
    adalah frame **terakhir yang diterima dari kamera**, bukan bitmap yang sedang tampil: frame
    yang datang saat UI thread masih sibuk sengaja dibuang, sehingga bitmap yang tampil bisa
    tertinggal beberapa frame. Untuk sumber foto, yang dipakai adalah foto itu sendiri dan kamera
    tidak disentuh. Hasilnya tampil di kotak **Hasil Verifikasi** (`Stretch="Uniform"`, jadi rasio
    foto tidak diubah). Tombol `Hapus Foto` membuang foto dan hasil verifikasi lalu kembali ke live.
  * **Satu foto per waktu, dibatasi GC.** Bitmap foto lama tidak di-`Dispose` saat diganti (alasan
    sama dengan bitmap live: renderer Avalonia bisa masih memakainya), dan jumlahnya paling banyak
    satu per upload sehingga dampak memorinya terbatas.
  * Pengenalan wajah, encoding, dan pemanggilan API Frista untuk foto ini **belum** ada.

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

`Apps:CameraIndex` (opsional, default `null`) mengunci index kamera OpenCV untuk live view.
Biarkan `null` untuk pencarian otomatis; isi angka (mis. `0`) bila mesin punya lebih dari satu
kamera dan kamera yang diinginkan bukan yang pertama ditemukan.

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
