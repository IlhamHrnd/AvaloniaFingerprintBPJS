# DESIGN.md — Spesifikasi UI/UX Aplikasi Verifikasi Sidik Jari & Wajah BPJS

> **Status:** Specification (implementation-ready) — dokumen ini **tidak** mengubah kode aplikasi.
> **Bahasa:** Indonesia. **Lokasi:** root repository.
> **Target platform:** Avalonia UI (FluentTheme) desktop Windows, `MinWidth=880`.
> **Referensi visual:** mockup "Fingerprint BPJS App" yang disetujui pada sesi perencanaan (Fluent-inspired clinical workstation, primary biru `#155EEF`, sidebar navy gelap, panel hasil dengan state eksplisit).

---

## Daftar Isi

1. [Tujuan & Ruang Lingkup](#1-tujuan--ruang-lingkup)
2. [Prinsip Desain](#2-prinsip-desain)
3. [Non-Goals & Keputusan Tetap](#3-non-goals--keputusan-tetap)
4. [Arah Visual & Palet Warna](#4-arah-visual--palet-warna)
5. [Sistem Tema & Token Desain](#5-sistem-tema--token-desain)
6. [Tipografi](#6-tipografi)
7. [Spacing, Radius, Elevasi](#7-spacing-radius-elevasi)
8. [Layout & Struktur Jendela](#8-layout--struktur-jendela)
9. [Spesifikasi Komponen](#9-spesifikasi-komponen)
10. [State & Interaksi](#10-state--interaksi)
11. [Aksesibilitas](#11-aksesibilitas)
12. [Privasi Biometrik (Constraint Wajib)](#12-privasi-biometrik-constraint-wajib)
13. [Peta Implementasi per File](#13-peta-implementasi-per-file)
14. [Asumsi & Item Terbuka](#14-asumsi--item-terbuka)
15. [Checklist Validasi (QA Manual)](#15-checklist-validasi-qa-manual)

---

## 1. Tujuan & Ruang Lingkup

Dokumen ini menjadi acuan tunggal (single source of truth) untuk tampilan dan perilaku visual aplikasi, mencakup:

| Area | File terkait (existing) |
|---|---|
| Resource tema & gaya global | `App.axaml` |
| Shell aplikasi (sidebar, header, status bar) | `Views/MainWindow.axaml` |
| Layar verifikasi sidik jari / wajah | `Views/VerifikasiView.axaml` |
| Layar kode booking | `Views/KodeBookingView.axaml` |
| Dialog login petugas | `Views/LoginWindow.axaml` |
| State & properti yang direferensikan UI | `ViewModels/MainWindowViewModel.cs`, `VerifikasiViewModel.cs`, `KodeBookingViewModel.cs`, `LoginViewModel.cs`, `Services/SessionService.cs`, `Services/AppSettings.cs` |

**Sifat dokumen:** dokumentasi saja. Tidak ada file kode/AXAML yang diubah sebagai bagian penyusunan spesifikasi ini. Semua perubahan di §13 adalah rencana implementasi.

## 2. Prinsip Desain

1. **Clinical workstation, bukan consumer app.** Informasi padat, hierarki jelas, dekorasi minimal. Petugas memakai aplikasi ini berulang kali per hari — efisiensi visual lebih penting daripada novelty.
2. **Fluent-inspired.** Mengikuti FluentTheme bawaan Avalonia; kustomisasi hanya pada warna/spacing via token, bukan menggambar ulang kontrol.
3. **State selalu eksplisit.** Tidak ada area UI yang "ambigu": setiap panel (terutama hasil verifikasi) punya state bernama (Empty / Processing / Success / Failure / NoData) dengan tampilan berbeda.
4. **Tidak ada data basi (stale).** Placeholder `"-"` atau hasil lama tidak boleh tampil saat verifikasi baru berjalan atau gagal; field dikosongkan, bukan disamarkan.
5. **Tema adalah kontrak token.** Semua warna berasal dari token semantik Light/Dark; hex hardcoded di AXAML dianggap bug implementasi.
6. **Privasi biometrik by design.** Foto wajah, citra sidik jari, template, dan encoding hanya hidup di memori selama dibutuhkan; UI tidak pernah menyediakan jalur penyimpanan (lihat §12).
7. **Aksesibel.** Kontras AA, focus ring terlihat, status tidak disampaikan oleh warna saja (selalu ada teks/ikon).

## 3. Non-Goals & Keputusan Tetap

Keputusan berikut **sudah dikonfirmasi** dan tidak dibuka lagi:

- `MinWidth=880` pada `MainWindow` **dipertahankan**. Konsekuensinya: **tidak ada layout stacked satu kolom** (responsif mobile-style) — layout dua kolom Verifikasi (`*,560`) tetap berlaku pada semua ukuran jendela yang diizinkan.
- `LoginWindow` **wajib disatukan** dengan gaya aplikasi utama (token yang sama); bahasa visual ungu `#5C2D91`, tombol FAB bulat, dan TextBox underline **dihapus**.
- Ketidakseimbangan panel hasil Verifikasi adalah **masalah tinggi vertikal** (kartu hasil lebih pendek daripada kartu kiri karena `VerticalAlignment="Top"`), **bukan** disebabkan `MaxWidth=560`. `MaxWidth=560` dipertahankan.
- Dokumen ini tidak merancang ulang alur bisnis (verifikasi BPJS, SEP, printout) — hanya presentasi visual dan state.

## 4. Arah Visual & Palet Warna

**Arah:** "Fluent-inspired clinical workstation" sesuai mockup — permukaan netral terang/gelap, satu warna primary biru yang tegas untuk aksi utama, sidebar navy gelap sebagai anchor navigasi, warna semantik (hijau/merah/kuning) hanya untuk status.

### 4.1 Palet inti

| Peran | Warna | Penggunaan | Batasan |
|---|---|---|---|
| **Primary** | `#155EEF` | Aksi utama (Ambil & Verifikasi, Login, Simpan), nav aktif, focus ring, link | Satu-satunya warna aksi; jangan dicampur dengan aksi sekunder |
| **Accent (teal)** | `#0F766E` | Highlight sekunder terbatas (mis. penanda mode Wajah, chip informasional) | **Sangat dibatasi** — tidak pernah untuk tombol aksi utama |
| **Success** | `#107C10` | Status terhubung, verifikasi berhasil, banner sukses | Selalu berpasangan dengan ikon/teks |
| **Danger** | `#C42B1C` | Error, gagal verifikasi, perangkat terputus, banner error | Selalu berpasangan dengan ikon/teks |
| **Warning** | `#B26B00` | State transitional (Searching, NoData) | Jarang; hindari alarm fatigue |
| **Neutral** | skala slate/gray | Background, surface, border, teks | Mayoritas pixels aplikasi |

### 4.2 Warna legacy yang digantikan

Warna lama tetap diakui selama transisi melalui **alias brush** (§5.4), tetapi nilai visualnya berpindah:

| Lama (hex hardcoded / brush) | Baru |
|---|---|
| `AccentBrush` `#0F6CBD` | `App.Primary.Default` `#155EEF` |
| Sidebar ungu `#5C2D91`, hover `#6B3FA3`, aktif `#7B52AB`, teks `#E6D9F5`/`#D7C7EA` (MainWindow & LoginWindow) | Token sidebar navy (§5.3) |
| Focus underline login `#5C2D91` | `App.Border.Focus` |
| Background jendela `#F4F5F7` (hardcoded) | `App.Background.Base` (nilai light tetap `#F4F5F7`) |
| Border kartu `#E5E7EB`, photo slot `#E5E7EB` | `App.Border.Default`, `App.Surface.PhotoPlaceholder` |
| `HeaderBrush` `#F3F4F6`, banner `#F3F4F6` | `App.Surface.Header`, `App.Surface.Banner` |
| `OkBrush` `#107C10`, `ErrorBrush` `#C42B1C`, `MutedBrush` `#6B7280` | `App.Success.Default`, `App.Danger.Default`, `App.Text.Muted` (nilai light tidak berubah) |

## 5. Sistem Tema & Token Desain

### 5.1 Mekanisme

- `App.axaml` memakai **`ResourceDictionary.ThemeDictionaries`** dengan kunci `Light` dan `Dark`. Setiap tema mendefinisikan **`Color`** (bukan brush) sebagai token primitif.
- **Brush dibagikan (shared dictionary)** dan merujuk warna via `DynamicResource`, sehingga satu instans brush otomatis berganti warna saat tema berubah:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.ThemeDictionaries>
      <ResourceDictionary x:Key="Light">
        <Color x:Key="App.Primary.Default">#155EEF</Color>
        <!-- ...token light lainnya... -->
      </ResourceDictionary>
      <ResourceDictionary x:Key="Dark">
        <Color x:Key="App.Primary.Default">#4C8DFF</Color>
        <!-- ...token dark lainnya... -->
      </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>

    <!-- Brush semantik: Color memakai DynamicResource agar ikut tema -->
    <SolidColorBrush x:Key="App.Primary.Brush" Color="{DynamicResource App.Primary.Default}" />
    <SolidColorBrush x:Key="App.Background.Base.Brush" Color="{DynamicResource App.Background.Base}" />

    <!-- Alias legacy (lihat 5.4) -->
    <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource App.Primary.Default}" />
  </ResourceDictionary>
</Application.Resources>
```

- **Aturan pemakaian di AXAML:**
  - Hex hardcoded (`Background="#F4F5F7"`, `BorderBrush="#E5E7EB"`, dst.) **dilarang** — ganti `{DynamicResource <Token>.Brush}`.
  - Referensi `{StaticResource AccentBrush}` yang sudah ada **tidak wajib diubah** (brush-nya sendiri theme-aware karena `Color`-nya `DynamicResource`), tetapi kode baru wajib memakai token `App.*`.
  - `RequestedThemeVariant="Light"` di `App.axaml` menjadi **nilai awal default**; switching runtime dilakukan lewat `Application.Current.RequestedThemeVariant = ThemeVariant.Parse(...)` dari `ThemeService` (§5.6). Opsi `System` = `ThemeVariant.Default`.

### 5.2 Konvensi penamaan token

Format: `App.<Kategori>.<Peran>[.<Varian>]` (asumsi konvensi — lihat §14 item A2).

| Kategori | Contoh | Isi |
|---|---|---|
| `App.Background.*` | `Base`, `Overlay` | Latar jendela, scrim overlay |
| `App.Surface.*` | `Card`, `Header`, `Sidebar`, `SidebarHover`, `SidebarActive`, `Control`, `Banner`, `PhotoPlaceholder`, `StatusChip` | Latar permukaan |
| `App.Border.*` | `Default`, `Control`, `Strong`, `Focus` | Garis & focus ring |
| `App.Text.*` | `Primary`, `Secondary`, `Muted`, `Placeholder`, `OnAccent`, `OnSidebar`, `OnSidebarMuted` | Warna teks |
| `App.Primary.*` | `Default`, `Hover`, `Pressed`, `Subtle` | Aksi utama |
| `App.Accent.*` | `Default`, `Subtle` | Teal terbatas |
| `App.Success.*` / `App.Danger.*` / `App.Warning.*` / `App.Info.*` | `Default`, `Subtle` | Status |
| `App.Space.*`, `App.Radius.*`, `App.Shadow.*`, `App.Font.*` | lihat §6–§7 | Non-warna |

Varian `Subtle` = latar tint untuk banner/chip; `Default` = warna solid untuk ikon/teks/tombol.

### 5.3 Tabel token warna (Light / Dark)

| Token | Light | Dark | Keterangan |
|---|---|---|---|
| `App.Background.Base` | `#F4F5F7` | `#0F172A` | Latar jendela & konten |
| `App.Background.Overlay` | `#80FFFFFF` | `#99000000` | Scrim "Login Required" |
| `App.Surface.Card` | `#FFFFFF` | `#1E293B` | Kartu (`Border.card`), header, status bar |
| `App.Surface.Header` | `#F3F4F6` | `#263244` | Alias `HeaderBrush` |
| `App.Surface.Sidebar` | `#101828` | `#0B1220` | Sidebar navy (kedua tema gelap) |
| `App.Surface.SidebarHover` | `#1D2A3F` | `#1E293B` | Hover nav item & kartu petugas |
| `App.Surface.SidebarActive` | `#155EEF` | `#1D4ED8` | Nav item aktif (primary) |
| `App.Surface.Control` | `#FFFFFF` | `#0F172A` | Latar TextBox/ComboBox |
| `App.Surface.Banner` | `#F3F4F6` | `#263244` | Banner status netral |
| `App.Surface.PhotoPlaceholder` | `#E5E7EB` | `#334155` | Slot foto kosong |
| `App.Surface.StatusChip` | `#FFFFFF` | `#1E293B` | Chip perangkat di status bar |
| `App.Border.Default` | `#E5E7EB` | `#334155` | Border kartu/pembatas |
| `App.Border.Control` | `#D1D5DB` | `#475569` | Border input/tombol sekunder |
| `App.Border.Strong` | `#9CA3AF` | `#64748B` | Border penekanan |
| `App.Border.Focus` | `#155EEF` | `#4C8DFF` | Focus ring 2px |
| `App.Text.Primary` | `#111827` | `#F1F5F9` | Judul, nilai data |
| `App.Text.Secondary` | `#374151` | `#CBD5E1` | Teks tubuh |
| `App.Text.Muted` | `#6B7280` | `#94A3B8` | Caption, label field (alias `MutedBrush`) |
| `App.Text.Placeholder` | `#9CA3AF` | `#64748B` | Watermark |
| `App.Text.OnAccent` | `#FFFFFF` | `#FFFFFF` | Teks di atas primary |
| `App.Text.OnSidebar` | `#E6EDF7` | `#E6EDF7` | Teks sidebar |
| `App.Text.OnSidebarMuted` | `#94A3B8` | `#94A3B8` | Caption sidebar |
| `App.Primary.Default` | `#155EEF` | `#4C8DFF` | Aksi utama (alias `AccentBrush`) |
| `App.Primary.Hover` | `#3B76F5` | `#6BA1FF` | Hover tombol primary |
| `App.Primary.Pressed` | `#0F4BD1` | `#3B76F5` | Pressed tombol primary |
| `App.Primary.Subtle` | `#E8F0FE` | `#16275E` | Latar tint primary |
| `App.Accent.Default` | `#0F766E` | `#2DD4BF` | Teal terbatas |
| `App.Accent.Subtle` | `#E6F4F2` | `#134E4A` | Latar tint teal |
| `App.Success.Default` | `#107C10` | `#54B054` | Alias `OkBrush` |
| `App.Success.Subtle` | `#E7F4E7` | `#1B3A22` | Banner sukses |
| `App.Danger.Default` | `#C42B1C` | `#F87171` | Alias `ErrorBrush` |
| `App.Danger.Subtle` | `#FBEAE8` | `#45262A` | Banner error |
| `App.Warning.Default` | `#B26B00` | `#FBBF24` | Searching/NoData |
| `App.Warning.Subtle` | `#FFF4E5` | `#4A3410` | Banner warning |
| `App.Info.Default` | `#155EEF` | `#4C8DFF` | Informasional = primary |
| `App.Info.Subtle` | `#E8F0FE` | `#16275E` | Banner info |

> Nilai dark theme adalah usulan awal; wajib divalidasi kontrasnya saat implementasi (§15). Token `Subtle` dipasangkan dengan teks `Default` senama.

### 5.4 Alias brush legacy (kompatibilitas tanpa breaking change)

Brush berikut **dipertahankan key-nya** di `App.axaml` agar seluruh `{StaticResource ...}` existing tetap berfungsi, tetapi nilainya menjadi alias token:

| Key lama | Menjadi |
|---|---|
| `AccentBrush` | `Color="{DynamicResource App.Primary.Default}"` |
| `OkBrush` | `Color="{DynamicResource App.Success.Default}"` |
| `ErrorBrush` | `Color="{DynamicResource App.Danger.Default}"` |
| `MutedBrush` | `Color="{DynamicResource App.Text.Muted}"` |
| `HeaderBrush` | `Color="{DynamicResource App.Surface.Header}"` |

Converter existing (`Base64ImageConverter`, `BoolToBrushConverter`/`StatusBrush`, `InverseBoolConverter`) tetap dipakai; `BoolToBrushConverter` dipastikan mengambil brush dari token hasil alias sehingga otomatis theme-aware. Alias dihapus hanya setelah seluruh referensi AXAML bermigrasi ke token `App.*` (fase akhir implementasi).

### 5.5 Gaya kelas (class styles) existing → baru

| Kelas (file) | Kondisi sekarang | Spesifikasi baru |
|---|---|---|
| `TextBlock.h1` (App) | 20 SemiBold | Tetap; tambah `Foreground` `App.Text.Primary` eksplisit |
| `TextBlock.h2` (App) | 15 SemiBold, margin bawah 8 | Tetap; foreground `App.Text.Primary` |
| `TextBlock.caption` (App) | 12, `StaticResource MutedBrush` | `DynamicResource App.Text.Muted.Brush` |
| `Border.card` (App) | `White`, border `#E5E7EB`, radius 8, padding 16 | `App.Surface.Card` / `App.Border.Default`, radius `App.Radius.L`, padding `App.Space.L`, opsional `BoxShadow` `App.Shadow.Card` |
| `Button.nav` (MainWindow) | foreground `#E6D9F5`, hover `#6B3FA3` | Foreground `App.Text.OnSidebar`, hover `App.Surface.SidebarHover`, tinggi 40, radius `App.Radius.M` |
| `Button.nav.selected` (MainWindow) | `#7B52AB` | Background `App.Surface.SidebarActive`, foreground `App.Text.OnAccent`, SemiBold |
| `Button.accent` (MainWindow) | `AccentBrush`, hover `#0D5AA7` | Primary button §9.1 (`App.Primary.Default/Hover/Pressed`) |
| `Button.icon` (MainWindow) | foreground `#6B7280`, hover `#E5E7EB` | `App.Text.Muted`, hover `App.Surface.Header`, radius `App.Radius.M` |
| `TextBox.underline`, `Button.btn-purple`, `Button.fab` (LoginWindow) | bahasa visual ungu terpisah | **Dihapus**; diganti komponen standar §9.1–9.2 (§8.7) |
| Override `ToolTip` transparan (MainWindow & LoginWindow) | tooltip tanpa latar/border | **Dihapus**; tooltip direstyle dengan token (§9.8) |

### 5.6 Preferensi tema & persistensi (item terbuka → rekomendasi)

**Fakta kode saat ini:** belum ada mekanisme tema apa pun (`RequestedThemeVariant="Light"` statis; pencarian `Theme` di codebase nihil). `AppSettings.Load()` membaca `appsettings.json` dari `AppContext.BaseDirectory` secara **read-only** (`reloadOnChange: false`, tanpa `Save`).

**Rekomendasi implementasi** (keputusan final = open item §14 A1):

1. Tambah `Services/ThemeService.cs` — pilihan `Light` | `Dark` | `System`, dipetakan ke `ThemeVariant.Light/Dark/Default`, di-set via `Application.Current.RequestedThemeVariant`.
2. Persistensi ke **file user-level terpisah**: `%LocalAppData%\AvaloniaFingerprint\ui-settings.json` — bukan `appsettings.json`, karena direktori instalasi (mis. `Program Files`) umumnya tidak writable dan `AppSettings` tidak punya jalur tulis.
3. File preferensi **hanya** berisi setting UI (tema). Dilarang memuat data biometrik/pasien apa pun (§12).
4. Dialog "Pengaturan Tema" (§9.7) dibuka dari tombol gear di status bar (§8.4); `MainWindowViewModel` menambah `SelectedTheme` + command apply (atau `ThemeService` di-inject ke VM).
5. Default bila file tidak ada: `Light` (paritas dengan perilaku sekarang).

## 6. Tipografi

**Font stack** (sudah dipakai di `MainWindow`/`VerifikasiView`, dipertahankan dan dijadikan token `App.Font.Default`):

```
Segoe UI Variable Text, Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, Ubuntu, Cantarell, Noto Sans, sans-serif
```

**Skala ukuran** (base `FontSize=13` pada jendela dipertahankan untuk densitas workstation):

| Token / kelas | Ukuran | Weight | Penggunaan | Mapping existing |
|---|---|---|---|---|
| `App.Type.H1` / `TextBlock.h1` | 20 | SemiBold | Judul jendela, judul halaman, judul overlay | `Classes="h1"` |
| `App.Type.H2` / `TextBlock.h2` | 15 | SemiBold | Judul kartu/seksi ("Identitas & Verifikasi", "Hasil Verifikasi") | `Classes="h2"` |
| `App.Type.NavLabel` | 13 | SemiBold (aktif) / Normal | Item navigasi sidebar | `Button.nav` |
| `App.Type.Body` | 13 | Normal | Teks tubuh, status bar, banner | default |
| `App.Type.BodyLarge` | 15 | Normal / SemiBold utk nilai penting | Nilai data hasil (No Kartu, Usia) | `FontSize="15"` inline → token |
| `App.Type.Caption` / `TextBlock.caption` | 12 | Normal | Label field, subtitle, versi build | `Classes="caption"` |
| `App.Type.Small` | 11 | Normal | Teks bantuan sangat sekunder | jarang |

Aturan:

- `FontSize` inline (mis. `FontSize="15"` pada nilai hasil, `FontSize="12"` pada banner) **dimigrasikan** ke kelas/token saat implementasi; selama transisi, nilai inline yang sudah konsisten dengan tabel di atas boleh dipertahankan.
- Nilai data pasien (No Kartu, Usia) memakai `App.Type.BodyLarge` + `Foreground App.Text.Primary`; labelnya `App.Type.Caption` + `App.Text.Muted`.
- NIK 16 digit: pertahankan `FontFamily` default (bukan monospace) namun beri `LetterSpacing` halus bila tersedia; validasi panjang ditangani `MaxLength=16` (existing).
- Judul aplikasi di sidebar: 2 baris, `TextWrapping="Wrap"` (existing), warna `App.Text.OnSidebar`.

## 7. Spacing, Radius, Elevasi

### 7.1 Spacing — skala 4px

| Token | Nilai | Penggunaan khas |
|---|---|---|
| `App.Space.XS` | 4 | Gap label–input, padding chip kecil |
| `App.Space.S` | 8 | Gap antar-elemen dalam baris tombol, padding status bar (vertikal) |
| `App.Space.M` | 12 | Padding nav item (vertikal 10–12), gap antar-field |
| `App.Space.L` | 16 | Padding kartu, margin antar-kartu (existing `Margin="16"`), gap StackPanel seksi |
| `App.Space.XL` | 24 | Margin halaman/content (rekomendasi; existing 20 → dinormalkan ke 24 saat implementasi), padding dialog |
| `App.Space.XXL` | 32 | Pemisah seksi besar |

Existing yang dipertahankan: `Border.card Padding=16`, `Grid Margin=16` (VerifikasiView), `Padding="20"` header/status bar (boleh tetap 20 selama transisi, target 24/16 sesuai tabel).

### 7.2 Radius

| Token | Nilai | Penggunaan |
|---|---|---|
| `App.Radius.S` | 4 | Banner kecil, tooltip |
| `App.Radius.M` | 6 | Tombol, input, chip, photo kecil (existing: tombol `CornerRadius=6`, banner 6) |
| `App.Radius.L` | 8 | Kartu (existing `Border.card`), photo slot 160×160, dialog |
| `App.Radius.XL` | 12 | Overlay card besar (login overlay) |
| `App.Radius.Pill` | 999 | Dot status (Ellipse — tidak perlu radius), badge |

### 7.3 Elevasi (shadow)

Avalonia `Border` mendukung `BoxShadows`. Aplikasi ini flat-first; shadow hanya pemisah halus:

| Token | Nilai | Penggunaan |
|---|---|---|
| `App.Shadow.Card` | `0 1 2 #0F101828, 0 1 3 #1A101828` | Kartu opsional di light theme; **dark theme: andalkan border** (`App.Border.Default`), shadow minimal |
| `App.Shadow.Popup` | `0 4 16 #1E000000` | Tooltip, flyout, dialog (menggantikan `BoxShadow="0 4 16 0 #1E000000"` hardcoded di LoginWindow) |
| `App.Shadow.None` | — | Sidebar, header, status bar (flat + border) |

## 8. Layout & Struktur Jendela

### 8.1 Shell `MainWindow`

Ukuran jendela **tidak berubah**: `Width=1120 Height=760`, `MinWidth=880 MinHeight=620`, `MaxWidth=1280`, `CanResize=True`, `WindowStartupLocation=CenterScreen`. Perubahan hanya pada warna/token:

- `Background="#F4F5F7"` → `{DynamicResource App.Background.Base.Brush}`
- `FontFamily` inline → `{StaticResource App.Font.Default}`; `FontSize=13` tetap.

Struktur grid `ColumnDefinitions="240,*"` dipertahankan:

```
┌────────────┬──────────────────────────────────────────┐
│ SIDEBAR    │ HEADER  (PageTitle + PageSubtitle)       │
│ 240px      ├──────────────────────────────────────────┤
│ - brand    │ KONTEN (ContentControl, CurrentPage)     │
│ - nav      │  - VerifikasiView / KodeBookingView      │
│ - petugas  ├──────────────────────────────────────────┤
│   (bottom) │ STATUS BAR (chip perangkat | versi | ⚙)  │
└────────────┴──────────────────────────────────────────┘
```

Pada `MinWidth=880`: konten efektif ≈ 640px — dua kolom Verifikasi (`*,560`) tetap muat (kolom kiri ≥ ~460px pada margin 16). **Tidak ada breakpoint/layout bertumpuk** (§3).

### 8.2 Sidebar navigasi

| Elemen | Existing | Spesifikasi baru |
|---|---|---|
| Container | `Border Background=#5C2D91`, padding 16,16,16,12 | `App.Surface.Sidebar.Brush`, padding tetap |
| Brand | "Aplikasi Verifikasi dan Registrasi Sidik Jari" `h1` putih + caption `#D7C7EA` | Judul `App.Text.OnSidebar`; caption `App.Text.OnSidebarMuted`; tambah ikon app (glyph `&#xE928;` Segoe Fluent Icons / fingerprint) 24px di atas judul, `Margin` bawah `App.Space.M` |
| Nav item | `Button.nav` (padding 12,10; radius 6; foreground `#E6D9F5`; hover `#6B3FA3`) | Tinggi 40, radius `App.Radius.M`, margin horizontal 0 (stack spacing `App.Space.XS`); ikon glyph (Verifikasi `&#xE928;`, Kode Booking `&#xE7C8;`) 16px + label; foreground `App.Text.OnSidebar`; hover `App.Surface.SidebarHover` |
| Nav aktif | `Button.nav.selected` `#7B52AB` | `App.Surface.SidebarActive` + `App.Text.OnAccent` + SemiBold; hanya satu item aktif (binding `Classes.selected` existing dipertahankan) |
| Kartu petugas | `Border #6B3FA3` radius 8 padding 12 | `App.Surface.SidebarHover`, radius `App.Radius.L`, border 1px `#FFFFFF14`; `PetugasName` (`App.Text.OnSidebar`, SemiBold, `TextTrimming`), `PetugasDetail` (`App.Text.OnSidebarMuted`, caption) |
| Tombol Login/Logout | `Button.accent` tinggi 34, radius 6 | Tinggi 36; label dari `LoginButtonText`; state `IsLoginBusy` → `ProgressRing` 16px menggantikan teks + disabled (§9.1); saat logged-in jadi varian subtle-on-dark (foreground `App.Text.OnSidebar`, border `App.Border.Strong`) |

Emoji `👆`/`🔖` pada label nav diganti glyph Segoe Fluent Icons (asumsi §14 A5). `ToolTipService.Delay` sidebar 200ms dipertahankan.

### 8.3 Header halaman

- `Border Background=White BorderBrush=#E5E7EB Padding="20,14,20,12"` → `App.Surface.Card.Brush` + `App.Border.Default.Brush`; border hanya bawah (existing `0,0,0,1`).
- `PageTitle` memakai `h1` (`App.Text.Primary`), `PageSubtitle` memakai `caption`.
- Tinggi header konsisten antar halaman; tidak ada kontrol interaktif di header (aksi berada di konten/status bar).

### 8.4 Status bar perangkat

- Container: `App.Surface.Card.Brush`, border atas `App.Border.Default.Brush`, padding horizontal 20 / vertikal 8 (tinggi efektif ±36).
- **Chip perangkat** (Fingerprint, Kamera) — struktur per chip: `Border` (background `App.Surface.StatusChip`, border `App.Border.Default`, radius `App.Radius.M`, padding `10,4`) berisi:
  - `Ellipse` 8px: `App.Success.Default` (Connected) / `App.Danger.Default` (Disconnected) / `App.Warning.Default` (Searching) / `App.Info.Default` (Busy/Streaming).
  - `TextBlock` Body: `"Fingerprint: {DeviceStatus}"` / `"Kamera: {CameraStatus}"`, warna `App.Text.Secondary`.
  - `ToolTip`: `DeviceTooltip` / `CameraTooltip` (detail + nama perangkat), gaya §9.8 (override transparan dihapus).
- Mapping state → warna memerlukan status searching yang saat ini hanya berupa teks (`FingerprintServices.SearchingStatus`, `CameraServices.SearchingStatus`); implementasi boleh mencocokkan teks status atau menambah enum/bool baru di VM (keputusan implementasi, §14 A8).
- Sisi kanan: teks versi `v{ApplicationVersion} · build {BuildTimestamp}` (`caption`) + **tombol ikon gear** `Button.icon` (glyph `&#xE713;`, `AutomationProperties.Name="Pengaturan Tema"`) yang membuka dialog §9.7.
- Prinsip aksesibilitas: dot warna **selalu** disertai teks status — warna bukan satu-satunya sinyal.

### 8.5 `VerifikasiView` — dua kolom & perbaikan keseimbangan vertikal

Struktur `Grid ColumnDefinitions="*,560"` dengan `Margin=16` **dipertahankan** (keputusan §3). Kolom kiri = kartu "Identitas & Verifikasi", kolom kanan = kartu "Hasil Verifikasi" (`MaxWidth=560`).

**Masalah yang diperbaiki (vertical imbalance):** kedua kartu saat ini `VerticalAlignment="Top"` sehingga kartu hasil (kanan) berhenti setinggi kontennya dan terlihat "menggantung" dibanding kartu kiri yang panjang. Spesifikasi perbaikan:

1. Kedua `Border.card` → `VerticalAlignment="Stretch"`; kolom berada pada satu `Grid` row sehingga tinggi keduanya otomatis setara.
2. Kartu kanan: `MinHeight="480"` agar state Empty tidak terlihat kosong; konten Empty dipusatkan secara vertikal di area sisa (row `*` pada `Grid RowDefinitions="Auto,Auto,*"` yang sudah ada).
3. Kartu kiri: konten tetap top-aligned; sisa ruang dibiarkan (tidak diisi dekorasi).
4. `MaxWidth=560` kanan **tidak diubah** — bukan penyebab masalah.
5. Scroll: jika konten kiri melebihi tinggi (mode Wajah + preview kamera 640×360), bungkus kolom kiri dengan `ScrollViewer` (`VerticalScrollBarVisibility=Auto`); kolom kanan tidak di-scroll kecuali konten hasil melebihi tinggi.

**Overlay "Login Required"** (`IsVisible="{Binding !IsLoggedIn}"`): scrim `App.Background.Overlay.Brush`; kartu tengah `App.Surface.Card`, radius `App.Radius.XL`, `BoxShadow App.Shadow.Popup`, padding `App.Space.XL`, max-width 420; ikon kunci glyph `&#xE72E;` 32px `App.Text.Muted`; judul `h1` "Login Required"; caption "Silakan login untuk mengaktifkan verifikasi."

**Kartu kiri — elemen & token:**

| Elemen | Existing | Baru |
|---|---|---|
| Photo slot | `Border 160×160 Background=#E5E7EB` radius 8, emoji 📷 + "Foto" | `App.Surface.PhotoPlaceholder.Brush`, radius `App.Radius.L`, ikon kamera glyph `&#xE722;` `App.Text.Muted` + label caption "Foto" |
| Input NIK | `TextBox` watermark "Masukkan NIK (16 digit)", `MaxLength=16`, `IsEnabled={!IsBusy}` | Standar §9.2 (tinggi 36, token control) |
| Mode radio | "Sidik Jari" (`IsFingerprintMode`) / "Wajah" (`IsWajahMode`) horizontal | Standar §9.2; opsi Wajah boleh diberi penanda `App.Accent.Default` saat aktif (satu-satunya pemakaian teal) |
| Tombol aksi sidik jari | "👆 Ambil & Verifikasi" (accent, 38), "Verifikasi Ulang" (38, `HasResult`), "Reset" | Primary L=40 §9.1; sekunder "Verifikasi Ulang" & "Reset" varian Secondary 36; glyph menggantikan emoji |
| Tombol aksi wajah | "📷 Ambil Foto", "🔗 Konek Kamera", "🔁 Verifikasi Wajah", "↩ Reset" dengan `IsVisible` kondisional | "Verifikasi Wajah" = Primary; "Ambil Foto"/"Konek Kamera" = Secondary; "Reset" = Ghost; visibilitas kondisional existing dipertahankan |
| Status scan | `ScanStatus` (accent, SemiBold) + `ProgressBar IsIndeterminate` tinggi 4 | Warna `App.Primary.Default`; progress bar tetap |
| Progress wajah | `FaceProgressText` + `ProgressBar Value={FaceProgressValue}` max 100 | Tetap; warna primary |
| Picker berkas | "Pilih Foto" (`.jpg/.png`, `PickFacePhotoCommand`) | Secondary S/M §9.1 |
| Preview kamera | `Border 640×360 Background=Black` + `FrameAImage`/`FrameBImage` | Latar hitam dipertahankan (video), radius `App.Radius.M`, border `App.Border.Default`; **frame tidak boleh disimpan** (§12) |
| Banner status | `Border #F3F4F6` + `StatusMessage` via `StatusBrush` | Banner §9.5 (varian success/danger dari `StatusIsError`) |

**Kartu kanan — hasil verifikasi:** lihat state lengkap di §10.1. Foto hasil (`ResultImageBase64` via converter `Base64Image`, `HasResultImage`) ditampilkan 160×160 `Uniform` radius `App.Radius.L`; field `No Kartu`/`Usia`/`Keterangan` label caption `App.Text.Muted` + nilai `App.Type.BodyLarge` `App.Text.Primary`; `ResultPlaceholderLabel` hanya muncul di state Empty.

### 8.6 `KodeBookingView`

- Root `Background=#F4F5F7` → `App.Background.Base.Brush`; `Grid Margin=16` tetap; kartu tengah `MaxWidth=760` dipertahankan.
- Input kode booking + tombol "Verifikasi" (Primary M, `IsEnabled={!IsBusy}`) dan "Reset" (Secondary M) tinggi dinormalkan 36.
- Busy: teks "Memproses kode booking..." (`App.Primary.Default`, SemiBold) + `ProgressBar IsIndeterminate` tinggi 4 — pola dipertahankan.
- Banner status (`HasStatus`, `StatusMessage`, `StatusIsError` via `StatusBrush`): `Background=#F3F4F6` hardcoded → komponen banner §9.5 (varian success/danger subtle + ikon), `TextWrapping=Wrap`.

### 8.7 `LoginWindow` — unifikasi dengan gaya utama

**Dipertahankan:** `Width=440 Height=380`, `CanResize=False`, `WindowStartupLocation=CenterScreen`, judul jendela, flow `LoginCommand`/`IsBusy`/`StatusMessage`/`StatusIsError`. Bila konten versi baru melebihi 380px, tinggi boleh disesuaikan ke 420 (keputusan implementasi).

**Dihapus (bahasa visual terpisah):**

- Style `TextBox.underline` (termasuk override `PART_BorderElement` dan focus ungu `#5C2D91`).
- Style `Button.btn-purple` (`#5C2D91`/`#4A2377`) dan `Button.fab` (FAB bulat 52×52 radius 999 + `BoxShadow` hardcoded).
- Override `ToolTip` transparan.

**Spesifikasi baru:**

1. `Window Background` → `App.Background.Base.Brush` (bukan `#F4F5F7` hardcoded) agar dark theme benar.
2. Layout: kartu terpusat `Border` `App.Surface.Card`, border `App.Border.Default`, radius `App.Radius.L`, padding `App.Space.XL`, margin `App.Space.XL` dari tepi jendela.
3. Header kartu: ikon app glyph 24px `App.Primary.Default` + judul "Login Petugas" (`h2`) + caption "Masuk untuk mengaktifkan verifikasi" (`App.Text.Muted`).
4. Field: `TextBox` standar §9.2 — Username (watermark "Username"), Password (`PasswordChar='•'`, `RevealPassword` checkbox dipertahankan).
5. Tombol utama: "Login" **Primary L (40px), full-width, radius `App.Radius.M`**, `IsDefault=True`; saat `IsBusy` → `ProgressRing` 16px + disabled (pengganti FAB).
6. Status: area `StatusMessage` memakai banner §9.5 (success/danger) alih-alih TextBlock polos.
7. `Esc` membatalkan/menutup dialog; focus awal di Username.

## 9. Spesifikasi Komponen

### 9.1 Button

| Varian | Background | Foreground | Border | Penggunaan |
|---|---|---|---|---|
| **Primary** | `App.Primary.Default` | `App.Text.OnAccent` | — | Satu aksi utama per konteks: Ambil & Verifikasi, Verifikasi Wajah, Login, Simpan, Verifikasi (Kode Booking) |
| **Secondary** | `App.Surface.Control` | `App.Text.Primary` | 1px `App.Border.Control` | Reset, Verifikasi Ulang, Pilih Foto, Batal |
| **Ghost/Subtle** | transparan | `App.Text.Secondary` | — | Aksi ringan (ikon gear, Reset wajah "↩") |
| **Danger** | `App.Danger.Default` | `App.Text.OnAccent` | — | Hanya bila ada aksi destruktif eksplisit (saat ini: Logout boleh tetap Secondary/Ghost) |

- Ukuran: **S=32** (jarang), **M=36** (default; menggantikan 34/38 existing), **L=40** (CTA utama). `MinWidth` 80 (M) / 120 (L).
- Radius `App.Radius.M`; `HorizontalContentAlignment=Center` (pola existing dipertahankan).
- State: hover → `App.Primary.Hover` (Primary) / `App.Surface.Header` (Secondary); pressed → `App.Primary.Pressed` / `App.Border.Strong`; disabled → opacity 45% + kursor default; busy → `ProgressRing` 16px menggantikan ikon/teks, `IsEnabled=false` (pola `IsLoginBusy`/`IsBusy` existing).
- Focus: ring 2px `App.Border.Focus` + offset 1px (default Fluent dipertahankan/ditegaskan).
- Ikon: glyph Segoe Fluent Icons 16px di kiri label, gap `App.Space.S`.

### 9.2 Input

- `TextBox`/`ComboBox`: tinggi 36, background `App.Surface.Control`, border 1px `App.Border.Control`, radius `App.Radius.M`, padding `10,6`; watermark `App.Text.Placeholder`.
- Focus: border 2px `App.Primary.Default` (menggantikan underline ungu login).
- Disabled: opacity 45%. Error (validasi NIK): border `App.Danger.Default` + teks bantuan caption `App.Danger.Default` di bawah input (`IsIdentityValid=false` && NIK terisi).
- `RadioButton` (mode Sidik Jari/Wajah): default Fluent, checked accent `App.Primary.Default`; tinggi hit-area ≥32; label Body.
- `CheckBox` (RevealPassword): default Fluent, label caption.

### 9.3 Card

`Border.card` (§5.5): `App.Surface.Card`, border 1px `App.Border.Default`, radius `App.Radius.L`, padding `App.Space.L`, shadow opsional light-theme. Judul kartu `h2` + `Margin` bawah `App.Space.M`.

### 9.4 Photo slot & foto hasil

- Slot kosong: 160×160, `App.Surface.PhotoPlaceholder`, radius `App.Radius.L`, ikon + label "Foto" di tengah (StackPanel vertikal, gap `App.Space.XS`).
- Foto terisi (preview capture / foto hasil `HasResultImage`): `Image Stretch=Uniform` di dalam `Border` radius `App.Radius.L` dengan clipping; tidak ada border tambahan.
- Preview kamera: 640×360 latar hitam; dua `Image` buffer A/B dipertahankan (pola anti-flicker existing).
- **Semua tampilan foto bersifat sementara** — lihat §12.

### 9.5 Banner status / notifikasi

Menggantikan pola `Border #F3F4F6 + TextBlock` dan notifikasi toast pada mockup (aplikasi tetap memakai **banner inline**, bukan toast — tidak ada infrastruktur toast, dan banner inline lebih dapat diaudit petugas):

| Varian | Background | Teks/ikon | Pemicu |
|---|---|---|---|
| Success | `App.Success.Subtle` | `App.Success.Default`, ikon ✓ `&#xE73E;` | `StatusIsError=false` (verifikasi/booking berhasil) |
| Danger | `App.Danger.Subtle` | `App.Danger.Default`, ikon ✕ `&#xE711;` | `StatusIsError=true` |
| Warning | `App.Warning.Subtle` | `App.Warning.Default`, ikon ⚠ `&#xE7BA;` | NoData / data tidak ditemukan |
| Info | `App.Info.Subtle` | `App.Info.Default`, ikon ℹ `&#xE946;` | Informasi netral |

Struktur: radius `App.Radius.M`, padding `10,12`, ikon 16px + teks Body `TextWrapping=Wrap`, gap `App.Space.S`. `BoolToBrushConverter` (`StatusBrush`) tetap dipakai untuk memilih brush teks/ikon; background banner memerlukan converter/trigger baru atau dua `Border` bertumpuk dengan `IsVisible` — keputusan implementasi. Banner tidak auto-dismiss (petugas harus sempat membaca); hilang saat Reset atau proses baru.

### 9.6 Chip status perangkat

Lihat §8.4. Tinggi ±24, radius `App.Radius.M`, dot `Ellipse` 8px + teks Body; hover menampilkan tooltip detail.

### 9.7 Dialog "Pengaturan Tema"

Mengikuti mockup: judul "Pengaturan Tema", isi `RadioButtons` vertikal **Light / Dark / System** (default mengikuti `ThemeService`), footer tombol **Simpan** (Primary M, `IsDefault`) dan **Batal** (Secondary M, `IsCancel`). Implementasi via `ContentDialog` (Fluent bawaan) atau `Window` modal 360×auto `WindowStartupLocation=CenterOwner`. Padding `App.Space.XL`, radius `App.Radius.L`, shadow `App.Shadow.Popup`. Perubahan tema diterapkan saat Simpan (bukan live-preview) agar sederhana dan mudah di-persist.

### 9.8 Tooltip

Menggantikan override transparan: background `App.Surface.Card`, border 1px `App.Border.Default`, radius `App.Radius.S`, padding `8,12`, teks caption `App.Text.Primary`, shadow `App.Shadow.Popup`, `MaxWidth=360`. `ToolTipService.Delay` 200–400ms.

### 9.9 Progress

- `ProgressBar` determinate: tinggi 4–6, track `App.Surface.Header`, fill `App.Primary.Default`; dipakai `FaceProgressValue` (0–100).
- `ProgressBar` indeterminate: tinggi 4; dipakai `IsScanning`/`IsBusy` kode booking.
- `ProgressRing`: 24–32px (spinner "Memverifikasi..."), 16px (dalam tombol busy).

## 10. State & Interaksi

### 10.1 Panel "Hasil Verifikasi" — lima state wajib

| # | State | Pemicu (properti VM existing) | Tampilan kartu hasil |
|---|---|---|---|
| 1 | **Empty** | `!HasResult && !IsVerifying && !IsVerifyingFace && !HasStatus` | Ikon placeholder `App.Text.Muted` (glyph hasil/dokumen) + `ResultPlaceholderLabel` di tengah vertikal. **Field disembunyikan; tanpa `"-"`.** Teks label per mode: sidik jari → "Hasil verifikasi sidik jari akan muncul di sini."; wajah → "Hasil verifikasi wajah akan muncul di sini." (sudah didukung `NotifyPropertyChangedFor` pada perubahan mode) |
| 2 | **Processing** | sidik jari: `IsBusy && IsScanning` (`ScanStatus`); wajah: `IsVerifyingFace` / `IsFaceProcessing` (`FaceProgressText`, `FaceProgressValue`) | `ProgressRing` + "Memverifikasi..." (pola `IsVerifying` existing). Data hasil sebelumnya **disembunyikan/di-reset lebih dulu** (`ResetResult()`), bukan ditumpuk. Tombol kiri disabled; progress determinate wajah tampil di kartu kiri |
| 3 | **Success** | `HasResult=true` && `StatusIsError=false` | Foto hasil 160×160 bila `HasResultImage`; field No Kartu / Usia / Keterangan terisi; banner Success (§9.5) dengan pesan `StatusMessage`; tombol "Verifikasi Ulang" muncul (existing `HasResult`) |
| 4 | **Failure** | `StatusIsError=true` (mis. `Verifikasi gagal: ...`, perangkat terputus saat memindai, timeout 15 dtk) | Banner Danger dengan pesan error lengkap + saran aksi ("Periksa perangkat / coba lagi"). Field hasil **kosong/tersembunyi** (`ResetResult()` sudah dipanggil di alur gagal — dipertahankan). CTA sekunder "Coba lagi" opsional |
| 5 | **NoData** | hasil API sukses tetapi data pasien tidak ditemukan (errors berisi "tidak ditemukan") | Banner Warning: "Data tidak ditemukan untuk NIK/kode tersebut." + field tersembunyi. **Dibedakan dari Failure generik** agar petugas tidak menyimpulkan kerusakan sistem. Implementasi: VM perlu penanda NoData (parsing pesan/ kode error) — keputusan implementasi §14 A8 |

**Aturan anti-stale (wajib):**

1. Setiap verifikasi baru dimulai → `ResetResult()` + `SetStatus(string.Empty, false)` **sebelum** proses I/O (pola existing di `VerifyAsync`/`EnterFingerprintMode` — dipertahankan dan diberlakukan sama untuk alur wajah & kode booking).
2. Nilai field default = **string kosong**, bukan `"-"`. `"-"` hanya boleh muncul di `DeviceDetail` status bar bila nama perangkat null (existing `state.DeviceName ?? "-"`) karena itu metadata perangkat, bukan data pasien.
3. Pola `_facePhotoGeneration` (penomoran generasi foto agar respons lama tidak menimpa hasil terbaru) **dipertahankan** — ini mekanisme anti-race yang wajib ada di spec UI.
4. Pindah mode (Sidik Jari ↔ Wajah) = reset penuh hasil + status + foto (existing `EnterFingerprintMode`/`EnterWajahMode` men-null-kan `CapturedPhoto`, `UploadedPhoto`, `_uploadedPhotoBytes`, `ClearFrames()` — dipertahankan).
5. Reset manual (`ResetCommand`/`ResetFaceCommand`) mengembalikan kartu ke state **Empty**, bukan Failure.

### 10.2 State perangkat (status bar & gating)

| State | Sumber | Dot | Teks contoh | Efek ke UI |
|---|---|---|---|---|
| Connected | `DeviceConnected` / `CameraConnected` = true | `App.Success.Default` | "Fingerprint: Connected" | Aksi verifikasi enabled (bila syarat lain terpenuhi) |
| Disconnected | = false | `App.Danger.Default` | "Kamera: Disconnected" | Tombol terkait disabled; banner peringatan di kartu kiri bila mode aktif membutuhkan perangkat itu |
| Searching | `FingerprintServices.SearchingStatus` / `CameraServices.SearchingStatus` | `App.Warning.Default` | "Fingerprint: Searching..." | Sama dengan Disconnected; tooltip menjelaskan sedang mencari perangkat |
| Busy/Streaming | `IsScanning` / `Camera.IsConnected` (streaming) | `App.Info.Default` | "Kamera: Streaming" | Progress ditampilkan; tombol capture disabled selama busy |

`CanVerify()` existing (`IsFingerprintMode && IsLoggedIn && DeviceConnected && !IsBusy && !IsVerifyingFace && !IsConnecting && IsIdentityValid`) adalah kontrak gating — UI hanya menampilkan state disabled sesuai kondisi ini, tanpa logika tambahan di view.

### 10.3 State sesi

- **Logged out:** overlay "Login Required" di VerifikasiView (§8.5); nav tab/halaman tetap terlihat; kartu petugas menampilkan "Belum login" + "Silakan login untuk mengaktifkan verifikasi" (existing `UpdateSessionState`).
- **Login busy:** `IsLoginBusy=true` → tombol Login/Logout menampilkan `ProgressRing`, disabled.
- **Logged in:** kartu petugas menampilkan `Nama` (fallback `Username`) + `"{Username} · login {HH:mm}"`; tombol menjadi "Logout"; preview kamera otomatis dimulai hanya bila mode Wajah (`EnsureCameraPreviewAsync` existing).
- **Logout:** `SessionService.SignOut()` → kamera stop preview, semua halaman kembali locked; hasil verifikasi terakhir **wajib di-reset** (privasi, §12).

### 10.4 State kontrol (umum)

| State | Perlakuan |
|---|---|
| Hover | Perubahan background satu tingkat (token Hover); transisi ≤150ms |
| Pressed | Token Pressed; tanpa animasi tambahan |
| Focus (keyboard) | Ring 2px `App.Border.Focus` — **tidak pernah dihilangkan** (`Focusable` default dipertahankan) |
| Disabled | Opacity 45%, tooltip opsional menjelaskan alasan (mis. "Perangkat tidak terhubung") |
| Loading | `ProgressRing` menggantikan ikon/teks; kontrol terkait disabled |
| Error | Banner Danger + (untuk input) border danger + teks bantuan |

## 11. Aksesibilitas

### 11.1 Kontras (target WCAG AA ≥ 4.5:1 teks normal)

Rasio dihitung analitis terhadap pasangan yang dipakai (validasi runtime tetap wajib saat QA — §15):

| Pasangan | Rasio | Status |
|---|---|---|
| `#155EEF` di atas `#FFFFFF` (primary sebagai teks/link) | 5.4:1 | ✓ AA |
| `#FFFFFF` di atas `#155EEF` (label tombol primary) | 5.4:1 | ✓ AA |
| `#FFFFFF` di atas `#0F766E` (accent terbatas) | 5.5:1 | ✓ AA |
| `#6B7280` di atas `#FFFFFF` (caption light) | 4.8:1 | ✓ AA |
| `#107C10` di atas `#FFFFFF` (success light) | 5.4:1 | ✓ AA |
| `#C42B1C` di atas `#FFFFFF` (danger light) | 5.7:1 | ✓ AA |
| `#F1F5F9` di atas `#1E293B` (text primary dark) | ≈12:1 | ✓ AA |
| `#94A3B8` di atas `#1E293B` (muted dark) | ≈5.7:1 | ✓ AA |
| `#E6EDF7` di atas `#101828` (teks sidebar) | ≈13:1 | ✓ AA |
| `#FFFFFF` di atas `#4C8DFF` (primary dark theme) | ≈3.1:1 | ✗ — **dark theme: teks tombol primary memakai `#0B1220`** atau primary dark digelapkan ke `#2E6FF3` (rasio ≈4.6:1). Keputusan final saat QA |

### 11.2 Aturan wajib

1. **Bukan warna saja:** setiap status punya teks dan/atau ikon (dot status bar selalu berdampingan label "Connected/Disconnected"; banner punya ikon + pesan).
2. **Focus visible:** focus ring 2px `App.Border.Focus` pada semua kontrol interaktif; urutan Tab logis: sidebar → header → konten (kartu kiri → kartu kanan) → status bar.
3. **Keyboard penuh:** semua aksi dapat dicapai tanpa mouse; `Enter` = tombol default (Login, dialog Simpan); `Esc` = batal/tutup dialog.
4. **Hit target:** tinggi kontrol interaktif ≥32px (tombol M=36, L=40; nav 40).
5. **AutomationProperties:** tombol ikon-only (gear tema, ikon nav bila label disembunyikan) wajib `AutomationProperties.Name`; contoh: `AutomationProperties.Name="Pengaturan Tema"`.
6. **Teks tidak dipotong tanpa alternatif:** `TextTrimming=CharacterEllipsis` selalu dipasangkan `ToolTip` (pola existing `PetugasName` dipertahankan).
7. Emoji dekoratif (👆📷🔒🔖) diganti glyph Segoe Fluent Icons + `AutomationProperties.Name` — emoji tidak konsisten antar-renderer dan tidak ramah screen reader (asumsi §14 A5).
8. Ukuran teks minimum 11px; caption 12px hanya untuk teks sekunder.

## 12. Privasi Biometrik (Constraint Wajib)

Data biometrik dalam aplikasi ini: citra sidik jari (`FingerImage` base64, `capture.VerificationTemplate`), frame kamera (`FrameAImage`/`FrameBImage`, `WriteableBitmap`), foto wajah (`CapturedPhoto`, `UploadedPhoto`, `_uploadedPhotoBytes`), foto hasil (`ResultImageBase64`), dan face encoding (alur `FaceRecognitionService`).

**Aturan desain yang mengikat implementasi:**

1. **Tidak ada persistensi untuk keperluan UI.** Foto/template/encoding/base64 hanya hidup di memori selama dibutuhkan tampilan atau transmisi API. Dilarang menulis ke disk: tidak ada cache gambar, tidak ada file temp, tidak ada screenshot otomatis, tidak ada "recent photos".
2. **Tidak ada logging payload biometrik.** `Trace.WriteLine` hanya boleh memuat metadata (nama state, ukuran, tipe error) — tidak pernah base64/template/encoding. (Pola existing sudah demikian; dipertahankan.)
3. **Bersihkan pada transisi:** Reset, pindah mode, ganti pasien (verifikasi baru), logout, dan tutup jendela → referensi gambar di-null-kan (pola `ResetResult`/`ClearFrames`/`EnterFingerprintMode` existing = baseline wajib).
4. **File konfigurasi & preferensi** (`appsettings.json`, `ui-settings.json` tema) tidak boleh memuat data biometrik atau data pasien apa pun.
5. **UI tidak menyediakan jalur ekspor foto:** tidak ada tombol simpan/salin gambar hasil; cetak (SEP/label) hanya memuat data non-biometrik.
6. **Preview kamera bersifat live:** frame double-buffer tidak diantrikan/disimpan; saat mode berganti atau halaman ditutup, streaming berhenti (`CameraUsageChanged` → `StopPreviewAsync` existing).
7. **Dokumen & mockup** tidak memuat sampel biometrik asli (placeholder/gambar sintetis saja).
8. Tampilan foto hasil dibatasi 160×160 di kartu hasil, hanya selama sesi verifikasi berjalan — memperkuat bahwa foto adalah umpan balik sesaat, bukan arsip.

## 13. Peta Implementasi per File

> Checklist rencana — **belum dieksekusi**; dokumen ini tidak mengubah kode apa pun. Urutan dianjurkan dari atas ke bawah agar aplikasi tetap dapat dijalankan di tiap langkah.

| # | File | Perubahan | Risiko |
|---|---|---|---|
| 1 | `App.axaml` | Tambah `ThemeDictionaries` (Light/Dark) berisi token `Color` §5.3; tambah brush semantik `App.*.Brush` (shared, `Color` via `DynamicResource`); konversi 5 brush legacy menjadi alias (§5.4); tambah token `App.Font.Default`, `App.Space.*`, `App.Radius.*`, `App.Shadow.*`; perbarui class styles (§5.5). `RequestedThemeVariant="Light"` dipertahankan sebagai default awal | Rendah — alias menjaga AXAML existing tetap jalan |
| 2 | `Services/ThemeService.cs` (baru) | Load/save preferensi `Light/Dark/System` ke `%LocalAppData%\AvaloniaFingerprint\ui-settings.json`; apply ke `Application.Current.RequestedThemeVariant` (§5.6) | Rendah |
| 3 | `Views/MainWindow.axaml` (+ `.cs` bila perlu) | Ganti hex hardcoded (`#F4F5F7`, `#5C2D91`, `#6B3FA3`, `#7B52AB`, `#E6D9F5`, `#D7C7EA`, `White`, `#E5E7EB`, `#0D5AA7`, `#6B7280`) dengan token; restyle sidebar (§8.2), chip status bar (§8.4), tombol gear tema; hapus override ToolTip transparan; glyph menggantikan emoji | Sedang — file terbesar |
| 4 | `ViewModels/MainWindowViewModel.cs` | Tambah `SelectedTheme`/command dialog tema; (opsional) ekspos state Searching untuk dot status bar (§10.2) | Sedang |
| 5 | `Views/LoginWindow.axaml` | Hapus style `underline`/`btn-purple`/`fab` & ToolTip override; terapkan layout kartu + token (§8.7); tombol Login primary full-width dengan state busy | Rendah |
| 6 | `Views/VerifikasiView.axaml` | Token semua permukaan; fix vertical balance (§8.5): `VerticalAlignment=Stretch` kedua kartu, `MinHeight=480` kartu kanan, Empty state terpusat; implementasi 5 state panel hasil (§10.1); banner status (§9.5); ScrollViewer kolom kiri | Sedang |
| 7 | `ViewModels/VerifikasiViewModel.cs` | Tambah penanda state NoData (bila diputuskan, §14 A8); pastikan reset status di semua alur masuk verifikasi | Sedang |
| 8 | `Views/KodeBookingView.axaml` | Token permukaan (`#F4F5F7`, `#F3F4F6`), tinggi tombol 36, banner status (§8.6) | Rendah |
| 9 | Dialog "Pengaturan Tema" (view baru / ContentDialog) | §9.7 | Rendah |
| 10 | Pembersihan alias | Setelah semua referensi bermigrasi, hapus brush legacy & `StatusBrush` lama bila tergantikan | Akhir fase |

## 14. Asumsi & Item Terbuka

| ID | Item | Status | Rekomendasi |
|---|---|---|---|
| A1 | **Persistensi preferensi tema** — belum ada mekanisme; `AppSettings` read-only dari `BaseDirectory` | Terbuka | `ThemeService` + `%LocalAppData%\AvaloniaFingerprint\ui-settings.json` (§5.6) |
| A2 | **Konvensi prefix token** `App.*` | Asumsi | Konfirmasi saat implementasi; alternatif `Fbp.*`. Yang penting konsisten & tidak bentrok key Fluent |
| A3 | Opsi **System theme** = `ThemeVariant.Default` (ikut OS) | Asumsi | Didukung Avalonia; uji di Windows light/dark |
| A4 | **Kontras dark theme** baru diverifikasi analitis (§11.1); pasangan `#FFFFFF` di atas `#4C8DFF` gagal AA | Terbuka | Saat QA: pakai teks gelap di atas primary dark, atau gelapkan primary dark ke `#2E6FF3` |
| A5 | Penggantian **emoji → Segoe Fluent Icons glyph** | Asumsi | Windows-only app; glyph aman. Bila ditolak, emoji dipertahankan (risiko konsistensi render) |
| A6 | **Sidebar navy gelap di kedua tema** (mengikuti mockup) | Asumsi | Nilai light/dark dibedakan halus (`#101828`/`#0B1220`) |
| A7 | **Accent teal `#0F766E`** hanya untuk penanda sekunder (mis. mode Wajah) | Asumsi | Jangan melebar ke aksi utama |
| A8 | **State NoData & Searching** butuh penanda eksplisit di VM (saat ini hanya teks pesan/bool connected) | Keputusan implementasi | Parsing pesan error "tidak ditemukan" → flag `IsNoData`; status searching → enum/bool baru |
| A9 | Tinggi `LoginWindow` 380 dipertahankan; bila layout baru membutuhkan, naik ke 420 | Keputusan implementasi | Ukur setelah konten final |

## 15. Checklist Validasi (QA Manual)

Jalankan setelah implementasi (tidak ada test UI otomatis di repo ini):

- [ ] Build & run: `dotnet run` — tidak ada error resource (key hilang → exception saat load).
- [ ] Toggle tema Light ↔ Dark ↔ System dari dialog; **semua layar** (Verifikasi, Kode Booking, Login, dialog) ikut berubah tanpa restart; tidak ada teks tak terbaca / permukaan "bocor" warna light di dark.
- [ ] Restart aplikasi → preferensi tema dipulihkan dari `ui-settings.json`.
- [ ] Resize jendela ke `MinWidth=880`: layout dua kolom tetap utuh, tidak ada overflow horizontal; sidebar tidak menyusut.
- [ ] Panel hasil: verifikasi kelima state §10.1 tampil benar; setelah Reset → Empty (tanpa `"-"`, tanpa data lama); verifikasi baru saat hasil lama tampil → hasil lama hilang lebih dulu.
- [ ] Logout → hasil & foto ter-reset; login → overlay hilang, preview kamera hanya di mode Wajah.
- [ ] Cabut/pasang perangkat: dot + teks status bar berubah (Connected/Disconnected/Searching), tooltip menampilkan nama perangkat.
- [ ] Keyboard-only: Tab menyeluruh, focus ring terlihat di kedua tema, Enter/Esc berfungsi di Login & dialog tema.
- [ ] Kontras: spot-check pasangan §11.1 dengan color picker pada runtime kedua tema (khususnya A4).
- [ ] **Privasi:** monitor folder aplikasi, `%LocalAppData%`, dan `%TEMP%` selama alur verifikasi sidik jari & wajah — tidak boleh ada file gambar/payload baru; log tidak memuat base64/template/encoding.
- [ ] Alias legacy: layar yang belum dimigrasi tetap tampil benar (brush alias theme-aware).

---

*Dokumen ini adalah spesifikasi; setiap penyimpangan saat implementasi wajib dicatat kembali di sini (versi dokumen dinaikkan).*










