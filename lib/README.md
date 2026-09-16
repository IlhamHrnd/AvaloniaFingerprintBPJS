# lib

Letakkan `DPUruNet.dll` (DigitalPersona U.are.U RTE SDK) di folder ini bila SDK tidak
terinstall di lokasi standar `C:\Program Files\DigitalPersona\U.are.U RTE\Windows\Lib\.NET\`.

`AvaloniaFingerprint.csproj` mendeteksi kedua lokasi tersebut secara otomatis:

* jika salah satu ada → dll direferensikan dan konstanta `HAS_DPURUNET` diaktifkan
  sehingga pemindaian sidik jari aktif;
* jika tidak ada → project tetap bisa di-build, tetapi setiap percobaan scan akan
  menampilkan pesan bahwa SDK belum tersedia.

DLL ini tidak ikut di-commit karena bukan komponen redistributable.
