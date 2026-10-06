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
  (`DeviceStatusEventArgs`). Kamera dipilih lewat **dua sinyal**, sama seperti watcher USB
  yang memakai `PNPClass = 'Biometric'`:

  **1) Pencocokan `cameraKeywords` — prioritas.** Device dianggap kamera bila namanya
  mengandung salah satu keyword dari array `cameraKeywords` di `Services/BaseService.cs`
  (urutan array = prioritas): `Webcam`, `Facecam`, `Integrated Camera`, `USB Video Device`,
  `UVC`, `Windows Virtual Camera`, lalu kelompok nama kamera bawaan laptop — `Integrated
  Webcam`, `Built-in Camera`, `Built-in Webcam`, `HD Camera`, `HD Webcam`, `FHD Camera`,
  `FHD Webcam`, `IR Camera`, `IR Webcam`, `Laptop Camera`, `Laptop Webcam`. Pencocokan tidak
  case-sensitive dan substring, sehingga `Integrated Camera`, `Integrated Camera HD`, dan
  `Integrated CAMERA` sama-sama cocok.

  **2) Fallback kelas PnP `Camera`.** Bila tidak ada keyword yang cocok, dipakai device yang
  Windows kelompokkan ke kelas `Camera` (`Win32_PnPEntity.PNPClass`). Kamera internal laptop
  sering memakai nama OEM yang tidak mengandung keyword apa pun — mis. `HD User Facing`
  (Quanta, `VID_0408`) di HP/ASUS/Acer/Lenovo — padahal devicenya nyata dan bisa dibuka
  OpenCV. Tanpa fallback ini mesin seperti itu melaporkan "kamera tidak ditemukan" dan
  `VideoCapture` bahkan tidak pernah dicoba. Kelas `Image` sengaja tidak ikut karena itu
  scanner/printer (mis. `EPSON L5290 Series`).

  Fallback dijalankan **setelah** loop keyword, jadi prioritas `cameraKeywords` tetap
  menentukan bila ada nama yang cocok — perilaku kamera bernama jelas maupun kamera HP tidak
  berubah. Menambah/mengubah daftar keyword dilakukan dengan mengedit array `cameraKeywords`
  di `Services/BaseService.cs`; array ini belum terikat ke `appsettings.json`. Perlu dicatat
  nama seperti `HD User Facing` tidak cocok dengan satu pun keyword di atas, jadi fallback
  kelas PnP-lah yang menemukannya.

  Keyword terakhir, `Windows Virtual Camera`, adalah jalur untuk kamera HP yang diekspos Windows
  lewat **Link to Windows / Phone Link**: Windows menampilkannya sebagai *software device*
  bernama `Windows Virtual Camera Device`. Karena pencocokan bersifat substring, keyword tersebut
  mengenai device itu tanpa kode khusus.

  Bila **kedua** sinyal kosong, kamera dianggap tidak ditemukan dan halaman Verifikasi Wajah
  menampilkan "Kamera internal laptop tidak ditemukan. Pastikan kamera tersedia dan tidak
  sedang digunakan oleh aplikasi lain." Device dengan status error/tidak hadir juga tidak
  dipakai. Bila device terdeteksi tetapi stream-nya gagal dibuka, pesannya dibedakan menjadi
  "Kamera terdeteksi tetapi tidak dapat digunakan" — penyebab tersering adalah kamera sedang
  dipakai aplikasi lain (Windows Camera, Teams, Zoom).

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
  * Pengenalan wajah untuk foto ini sudah tersedia sebagai service berdiri sendiri
    (`FaceRecognitionService`, lihat bagian [Pengenalan wajah](#pengenalan-wajah-encoding-128-nilai)),
    tetapi **belum disambungkan** ke tombol Verifikasi Wajah maupun ke API Frista.

## Konfigurasi

`appsettings.json` (disalin ke folder output) menggantikan `App.config`:

```json
{
  "Bpjs":        { "LoginUrl": "...", "VerifyUrl": "...", "RegistUrl": "...", "TimeoutSeconds": 20 },
  "Fingerprint": { "CaptureTimeoutMs": 7000, "VerifyTimeoutMs": 5000, "FingersPerHand": 4,
                   "RightHandIndex": 1, "LeftHandIndex": 6 },
  "Device":      { "PollIntervalSeconds": 2, "MatchKeywords": [ "U.are.U", "DigitalPersona", ... ] },
  "Storage":     { "RegistrationsFile": "registrations.json" },
  "FaceRecognition": { "ModelDirectory": "FaceModels", "Detector": "Cnn", "Upsample": 0,
                       "LandmarkModel": "Small", "NumJitterings": 1, "MaxEdge": 800 }
}
```

`Apps:CameraIndex` (opsional, default `null`) mengunci index kamera OpenCV untuk live view.
Biarkan `null` untuk pencarian otomatis; isi angka (mis. `0`) bila mesin punya lebih dari satu
kamera dan kamera yang diinginkan bukan yang pertama ditemukan.

Kunci `FaceRecognition` dijelaskan di bagian [Pengenalan wajah](#pengenalan-wajah-encoding-128-nilai).

## SDK DigitalPersona

`DPUruNet.dll` tidak redistributable, jadi referensinya bersyarat — lihat
[lib/README.md](lib/README.md). Tanpa SDK aplikasi tetap jalan dan bisa diuji, hanya
pemindaian yang menampilkan pesan bahwa SDK belum tersedia.

## Pengenalan wajah (encoding 128 nilai)

`Services/FaceRecognitionService.cs` adalah service **berdiri sendiri** yang menerima gambar dan
mengembalikan `List<double>` berisi **tepat 128 nilai** (vektor embedding wajah dlib ResNet v1).
Dibangun di atas [`FaceRecognitionDotNet`](https://www.nuget.org/packages/FaceRecognitionDotNet)
(pembungkus dlib), dengan OpenCvSharp yang sudah ada di project untuk konversi pixel.

> **Belum terintegrasi.** Alur verifikasi wajah yang ada (`VerifikasiWajahViewModel.VerifyFace()`)
> dan `FingerprintServices` tidak diubah, tidak ada UI yang
> memanggil service ini, dan tidak ada pengiriman ke Frista. Service hanya terdaftar di
> `AppServices.FaceRecognition` dan siap dipakai. Perbandingan 1:1 juga di luar cakupan;
> yang disediakan baru pembuatan vektor (plus helper `EuclideanDistance` untuk pengujian).

### Kontrak

Dijamin oleh implementasi dan diverifikasi harness (lihat di bawah):

* Hasil **selalu tepat 128 nilai**, semuanya finite (bukan `NaN`/`Infinity`), bukan vektor nol.
* Gagal berarti **melempar**, tidak pernah mengembalikan `null` atau list kosong.
* **Tepat satu wajah** per gambar. Wajah pertama tidak pernah dipilih diam-diam dari gambar
  berisi banyak orang — encoding orang yang salah jauh lebih berbahaya daripada permintaan ulang.
* Ketiga jalur masukan menghasilkan vektor **identik** untuk pixel yang sama, jadi vektor dari
  foto upload bisa dibandingkan dengan vektor dari tangkapan kamera.

### Tiga jalur masukan

| Method | Sumber di app | Catatan |
|---|---|---|
| `GetFaceEncodingFromFileAsync(path)` | foto yang dipilih lewat dialog | format: JPEG, PNG, BMP, TIFF, WebP |
| `GetFaceEncodingAsync(byte[])` | byte file upload | bukan `ReadOnlySpan<T>` karena harus bertahan melewati `await` |
| `GetFaceEncodingAsync(bgra, w, h, stride)` | `CameraFrameEventArgs.Pixels` / `WriteCaptureBgra` | pixel **BGRA8888**, `stride = 0` berarti rapat (`width * 4`) |

Jalur BGRA membaca buffer **secara sinkron sebelum `await` pertama**, lalu bekerja pada salinan
RGB miliknya sendiri. Karena itu buffer yang dipinjam dari `ArrayPool` aman dikembalikan pemanggil
segera setelah method mengembalikan `Task`-nya — harness membuktikan ini dengan mengosongkan
buffer di tengah pemanggilan dan memverifikasi vektornya tidak berubah.

### Exception

Semua turunan `FaceRecognitionExceptions` (`Exceptions/FaceRecognitionExceptions.cs`):

| Tipe | Arti |
|---|---|
| `FaceModelMissingExceptions` | model `.dat` atau native dlib tidak ada; pesan menyebut file yang kurang dan skrip unduhnya |
| `NoFaceDetectedExceptions` | 0 wajah — kondisi lapangan normal, tampilkan sebagai pesan, bukan crash |
| `MultipleFacesDetectedExceptions` | >1 wajah; `FaceCount` berisi jumlahnya |
| `FaceEncodingExceptions` | wajah ditemukan tapi vektornya rusak (panjang ≠ 128, atau ada NaN/Infinity) |
| `FaceImageExceptions` | masukan tidak usable: null, kosong, bukan gambar, dimensi/stride tidak konsisten |

### Setup: unduh model dlib (sekali)

`FaceRecognitionDotNet` tidak menyertakan file model, jadi empat file `.dat` (~126 MB) harus
diunduh lebih dulu ke `FaceModels/`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\download-face-assets.ps1
```

Skrip ini idempotent dan memverifikasi ukuran tiap file, jadi aman dijalankan ulang; file yang
sudah lengkap dilewati. Tambahkan `-SkipTestImages` bila hanya butuh modelnya. File `.dat`
**tidak di-commit** (lihat `.gitignore`) dan berasal dari
[`ageitgey/face_recognition_models`](https://github.com/ageitgey/face_recognition_models)
berlisensi CC0-1.0 — model yang sama dengan library Python `face_recognition`, sehingga vektornya
bisa dibandingkan silang dengan implementasi Python.

Tanpa model, app tetap build dan jalan; hanya panggilan encoding yang melempar
`FaceModelMissingExceptions` dengan pesan berisi nama file yang kurang dan perintah di atas.
`AvaloniaFingerprint.csproj` menyalin `FaceModels\*.dat` ke folder output lewat
`CopyToOutputDirectory=PreserveNewest`.

> Model **sengaja tidak** diletakkan di `Assets\`. Folder itu adalah glob `AvaloniaResource`,
> jadi 126 MB akan tertanam ke dalam `AvaloniaFingerprint.dll`.

### Konfigurasi

```jsonc
"FaceRecognition": {
  "ModelDirectory": "FaceModels",  // relatif = terhadap AppContext.BaseDirectory
  "Detector": "Cnn",               // "Cnn" (default, tangguh) | "Hog" (cepat, rapuh)
  "Upsample": 0,                   // 0 = deteksi di resolusi kerja; 1 = gandakan (4x lebih lambat)
  "LandmarkModel": "Small",        // "Small" = shape_predictor_5 | "Large" = shape_predictor_68
  "NumJitterings": 1,              // pengulangan landmark untuk akurasi; 1 sudah cukup
  "MaxEdge": 800                   // sisi terpanjang maksimum (px) sebelum diperkecil; 0 = nonaktif
}
```

Nilai yang tidak dikenal atau tidak ada jatuh ke default di atas dan **tidak** membuat fitur mati.
Model dimuat **malas** pada pemanggilan pertama (terukur ~1.1 detik), jadi startup app tidak
terpengaruh walaupun fitur wajah tidak pernah dibuka.

Catatan: `FaceRecognition.Create()` memvalidasi **keempat** file model, termasuk
`shape_predictor_68_face_landmarks.dat` yang tidak dipakai saat `LandmarkModel` = `Small`.
Jadi file itu tetap wajib diunduh.

### Kinerja (terukur)

Angka di bawah dari `dotnet run ... benchmark` (lihat bagian harness) di mesin pengembangan
Windows 10 / x64. Kolom menunjukkan waktu satu panggilan encoding penuh (deteksi + vektor):

| Konfigurasi | kamera 640x480 | dibatasi 800 px | asli 910x1137 |
|---|---|---|---|
| `Hog`, `Upsample: 1` | **gagal deteksi** | 544 ms | 682 ms |
| `Hog`, `Upsample: 0` | **gagal deteksi** | 295 ms | 347 ms |
| `Cnn`, `Upsample: 0` | 5 618 ms | 7 289 ms | 14 330 ms |
| `Cnn`, `Upsample: 1` | 17 811 ms | 27 996 ms | 59 172 ms |

Dua kesimpulan yang menentukan nilai default:

* **`Hog` tidak bisa dipakai.** Ia ~20x lebih cepat, tapi gagal mendeteksi wajah pada frame
  kamera 640x480 — persis jalur utama app ini. `Cnn` berhasil di semua ukuran yang diuji.
* **`Upsample: 1` membuang waktu 3-4x** tanpa menambah keberhasilan deteksi pada ukuran-ukuran
  ini. Karena itu default-nya `0`.

Default (`Cnn` + `Upsample: 0` + `MaxEdge: 800`) berarti sekitar **5-8 detik per gambar** di
mesin ini. Itu cukup untuk penangkapan sekali tekan (tombol *Verifikasi Wajah*), tetapi
**terlalu lambat untuk memproses setiap frame live camera**. Angka ini juga sangat bergantung
CPU: native DlibDotNet tidak dibangun dengan AVX2, sehingga di mesin dengan AVX2 bisa beberapa
kali lebih cepat. Turunkan `MaxEdge` (mis. `640`) bila perlu lebih cepat.

### Harness uji (`tools/FaceEncodingProbe`)

Konsol berdiri sendiri yang membuktikan kontrak di atas terhadap **native dlib sungguhan** —
bukan mock. Ia me-*reference* `AvaloniaFingerprint.csproj`, jadi yang diuji adalah
`FaceRecognitionService` yang sama persis dengan yang dipakai app.

```bash
# dari root repo; butuh model + gambar uji (lihat skrip unduh di atas)
dotnet build tools\FaceEncodingProbe\FaceEncodingProbe.csproj -c Debug
tools\FaceEncodingProbe\bin\Debug\net8.0-windows\FaceEncodingProbe.exe

# atau sekalian jalan:
dotnet run --project tools\FaceEncodingProbe\FaceEncodingProbe.csproj -c Debug
```

Exit code `0` = semua lulus, `1` = ada assertion gagal, `2` = prasyarat belum diunduh.
Terakhir dijalankan: **58 assertion lulus, 0 gagal** (~2 menit; sebagian besar waktunya adalah
deteksi CNN).

Yang dicakup:

| # | Kasus |
|---|---|
| 0 | biaya inisialisasi model diisolasi dari biaya deteksi |
| 1 | path file → 128 nilai valid |
| 2 | byte JPEG di memori → 128 nilai valid |
| 3 | pixel BGRA8888 (simulasi kamera) → 128 nilai valid |
| 3b | pixel BGRA dengan **padding stride 64 byte/baris diisi 0xFF** → hasil identik (stride benar) |
| 4 | gambar tanpa wajah → `NoFaceDetectedExceptions` |
| 5 | dua wajah → `MultipleFacesDetectedExceptions` dengan `FaceCount == 2` |
| 6 | 9 varian masukan rusak → `FaceImageExceptions` (null, kosong, bukan gambar, path tidak ada, dimensi nol, buffer pendek, stride kecil) |
| 7a | gambar sama dienkode dua kali → jarak tepat `0` |
| 7b | orang berbeda (obama vs biden) → jarak `0.85 > 0.6` |
| 7c | orang sama lewat jalur berbeda → jarak tepat `0` |
| 8 | model hilang → `IsReady == false` + pesan menyebut skrip unduh |
| 9 | `CancellationToken` yang sudah dibatalkan → `OperationCanceledException` |
| 10 | 4 pemanggilan paralel → semuanya valid dan identik (gate serialisasi bekerja) |
| 11 | buffer `ArrayPool` **dikosongkan sebelum `await`** → vektor tidak berubah |
| 12 | setelah `Dispose` → `ObjectDisposedException`; `Dispose` idempotent |
| 13 | `MaxEdge=800` vs `MaxEdge=0` → jarak `0.08`, identitas tidak berubah |

Tambahkan argumen `benchmark` untuk mencetak ulang tabel kinerja tanpa menjalankan kasus
fungsional:

```bash
tools\FaceEncodingProbe\bin\Debug\net8.0-windows\FaceEncodingProbe.exe benchmark
```

Folder `tools\` dikecualikan dari build app lewat `<Compile Remove="tools\**" />` di
`AvaloniaFingerprint.csproj` — tanpa itu, glob compile default SDK akan menyapu `Program.cs`
harness ke dalam app dan menghasilkan error "more than one entry point".

### Catatan implementasi (jebakan yang sudah dilewati)

* **`LoadImage` dan `LoadImageFile` bersifat statis**, bukan method instance. Hanya
  `FaceLocations` / `FaceEncodings` yang butuh instance hasil `FaceRecognition.Create()`.
* **Urutan parameter `LoadImage` adalah `(byte[] array, int ROW, int COLUMN, int STRIDE, Mode)`**
  — bukan `(width, height, channels)`. `stride` adalah jumlah **byte** per baris. Salah urutan
  menghasilkan `ArgumentOutOfRangeException: stride is less than column` dari DlibDotNet.
* **`Mode` hanya punya `Rgb` dan `Greyscale`** — tidak ada `Bgr`/`Bgra`. Padahal kamera memberi
  BGRA8888 dan `Cv2.ImDecode` memberi BGR, jadi konversi warna wajib dilakukan sebelum masuk dlib.
* **`Location` adalah struct murni** dan tidak mengimplementasikan `IDisposable` (compiler
  menolak `as IDisposable` lewat CS0039), jadi tidak ada resource native yang perlu dilepas.
  Yang wajib di-`Dispose` hanya `Image` dan `FaceEncoding`.
* **Ctor `new Mat(rows, cols, type, IntPtr, step)` sudah obsolete** di OpenCvSharp 4.13;
  penggantinya `Mat.FromPixelData(...)`. Mat itu membungkus memori pemanggil tanpa memilikinya,
  jadi `Dispose()`-nya tidak menyentuh array asli.
* **Satu decoder untuk semua jalur.** Awalnya file didekode dlib dan byte didekode OpenCV,
  sehingga dua decoder bisa memberi pixel sedikit berbeda untuk gambar yang sama. Sekarang
  semuanya lewat OpenCV, dan harness memverifikasi jaraknya tepat `0`.

## Build & jalankan

```bash
dotnet run --project AvaloniaFingerprint.csproj
```

Perintah itu sudah cukup. `AvaloniaFingerprint.csproj` memakai `<Platforms>x64;x86</Platforms>`,
dan build tanpa parameter `-p:Platform` otomatis memakai **x64** dengan output di
`bin\x64\<Configuration>\net8.0-windows\`. Native dlib/OpenCV tidak disalin ke root output
melainkan ke `runtimes\win-x64\native\` (layout RID standar) dan diselesaikan lewat
`AvaloniaFingerprint.deps.json`.

> **AnyCPU tidak didukung.** Native dlib hanya tersedia sebagai `win-x64` dan `win-x86`,
> sementara `FaceRecognitionDotNet.props` menyusun path native dari
> `runtimes\win-$(Platform)\native\`. Dengan `Platform=AnyCPU` path itu menjadi `win-AnyCPU`
> yang memang tidak ada, sehingga build gagal. Karena itu AnyCPU dihapus dari daftar platform.
> Memaksanya lewat `-p:Platform=AnyCPU` tetap ditolak, tapi dengan pesan
> `[Face] Platform 'AnyCPU' tidak didukung...` yang menjelaskan sebabnya — bukan MSB3030
> "Could not copy the file" yang membingungkan.

## Catatan perbedaan dari versi WinForms

* `DaftarFingerprint` di WinForms masih form kosong. Di sini tab tersebut diisi daftar
  registrasi yang dikirim dari komputer ini (`registrations.json`), lengkap dengan
  pencarian dan pratinjau gambar sidik jari.
* Kredensial petugas tidak lagi di-hardcode di `LoginPetugas.cs`; diisi lewat dialog login.
* RestSharp diganti `HttpClient`, `System.Drawing` diganti SkiaSharp
  (lihat `Services/ImageCodec.cs`), dan `AutoClosingMessageBox` diganti pesan inline.
* Verifikasi tidak lagi menulis ke tabel `PatientBiometric` — kode itu sudah dikomentari
  di form aslinya dan membutuhkan EntitySpaces/.NET Framework.
