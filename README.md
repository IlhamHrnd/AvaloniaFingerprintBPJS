# AvaloniaFingerprint

Aplikasi Avalonia (.NET 8, Windows) hasil ekstraksi tiga form fingerprint dari
project WinForms `KonfirmasiPendaftaranOnline`:

| Tab WinForms | Tab Avalonia |
|---|---|
| `DaftarFingerprint` | **Daftar Fingerprint** |
| `VerifikasiFingerprint` | **Verifikasi Fingerprint** |
| `RegistrasiFingerprint` | **Registrasi Fingerprint** |

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
