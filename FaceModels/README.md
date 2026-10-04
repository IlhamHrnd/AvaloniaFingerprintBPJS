# FaceModels

File model dlib yang dibutuhkan `FaceRecognitionDotNet`. **Tidak ikut di-commit**
(lihat `.gitignore`) karena totalnya ~126 MB dan bisa diunduh ulang kapan saja.

## Cara mengambil

Dari root repo:

```powershell
powershell -ExecutionPolicy Bypass -File tools\download-face-assets.ps1
```

Skrip tersebut idempotent — file yang ukurannya sudah benar akan dilewati.
Tanpa keempat file ini `FaceRecognitionService` melempar
`FaceModelMissingExceptions` saat pertama kali dipakai, dan halaman
**Verifikasi Wajah** tidak bisa menghasilkan encoding.

## Isi folder

| File | Ukuran | Dipakai untuk |
|---|---|---|
| `mmod_human_face_detector.dat` | 729.940 B | Deteksi wajah CNN (`Detector: "Cnn"`) |
| `shape_predictor_5_face_landmarks.dat` | 9.150.489 B | Landmark 5 titik — dipakai `LandmarkModel: "Small"`, jalur default encoding |
| `shape_predictor_68_face_landmarks.dat` | 99.693.937 B | Landmark 68 titik — dipakai `LandmarkModel: "Large"` |
| `dlib_face_recognition_resnet_model_v1.dat` | 22.466.066 B | ResNet penghasil vektor 128 dimensi |

`FaceRecognition.Create(directory)` memvalidasi keberadaan **keempat** file dan
melempar `FileNotFoundException` bila salah satunya hilang — jadi
`shape_predictor_68_face_landmarks.dat` tetap wajib ada walaupun konfigurasi
default memakai model 5 titik.

## Sumber & lisensi

* Repo: <https://github.com/ageitgey/face_recognition_models>
  (folder `face_recognition_models/models`, disimpan **tanpa** kompresi bz2)
* Lisensi: **Creative Commons Zero v1.0 Universal (CC0-1.0)** — public domain,
  bebas dipakai dan didistribusikan ulang tanpa atribusi.
* Model ini adalah model yang sama yang dipakai library Python
  [`face_recognition`](https://github.com/ageitgey/face_recognition), sehingga
  vektor 128 yang dihasilkan dapat dibandingkan silang dengan implementasi Python.

## Penempatan saat runtime

`AvaloniaFingerprint.csproj` menyalin `FaceModels\*.dat` ke folder output
(`CopyToOutputDirectory=PreserveNewest`), dan `appsettings.json` menunjuk ke
sana lewat `FaceRecognition:ModelDirectory` (path relatif diselesaikan terhadap
`AppContext.BaseDirectory`).

> Folder ini **sengaja bukan** `Assets\Models`. `AvaloniaFingerprint.csproj`
> punya `<AvaloniaResource Include="Assets\**" />`, yang akan menanam 126 MB
> file model ke dalam `AvaloniaFingerprint.dll`.
