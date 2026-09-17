# PostgresBackup — 設計規範系統 (Design System)

本文件定義 PostgresBackup WPF 圖形介面的視覺標準、主題色彩、字體排印與元件模式，以確保所有視圖（Settings, Backup, Restore, History, Log）具備高度統一的視覺語言與流暢的使用者體驗。

## 1. 核心調色盤 (Color Palette)

### 主題色彩 (Brand & Primary)
- **PrimaryColor**: `#2E7D32` (Emerald / Forest Green) — 用於導航欄頂部、卡片標頭、主按鈕、焦點邊框與安全狀態。
- **DarkPrimaryBrush**: `#1B5E20` — 用於按鈕按下狀態、深色徽章、重點邊框。
- **LightPrimaryBrush**: `#4CAF50` — 用於懸浮狀態 (Hover)、次要亮點與指示燈。
- **PrimaryTextBrush**: `#212121` — 主文字色彩，確保高對比度可讀性。
- **SecondaryTextBrush**: `#757575` — 輔助文字、標籤與次要說明。

### 狀態色彩 (Semantic Badges)
- **Success / Ready**: `#2E7D32` (綠色徽章，白字)
- **Warning / Incompatible**: `#F57C00` (橙黃色徽章，白字)
- **Danger / NotFound / Overwrite Risk**: `#D32F2F` (紅色徽章，白字)
- **Info / Running**: `#1976D2` (藍色徽章，白字)

### 介面背景與容器 (Surfaces & Regions)
- **Window Background / RegionBrush**: `#FAFAFA` (淺灰底色，乾淨俐落)
- **SecondaryRegionBrush**: `#F5F5F5` (左側導航側邊欄底色)
- **Card Background**: `#FFFFFF` (純白卡片本體)
- **BorderBrush**: `#E0E0E0` (細膩淡灰分界線，厚度 1px)

### 終端日誌主控台 (Terminal Console)
- **Console Background**: `#181818` (極簡深色)
- **Console Text**: `#E0E0E0` (淺白等寬字體)
- **Console Accent**: `#4EC9B0` (高亮參數與時間戳)
- **Console Font**: `Consolas, Courier New, monospace`, 12px

## 2. 版面與間距 (Layout & Spacing)

### 導覽側邊欄 (Sidebar Navigation)
- 寬度：`200px`，位於最左側。
- 標頭：綠寶石色塊（`Padding="20,18"`），包含盾牌圖標 `🛡️` (28px)、`PostgresBackup` 標題 (15px SemiBold, White) 與副標題 (10px, 80% White)。
- 導航選項：自訂 `RadioButton`（`NavMenuRadioButtonStyle`），選中時背景為 `PrimaryBrush`，文字為白色，圓角 `6px`。
- 底部：包含語系切換器與版本標籤（`v1.0.0`）。

### 內容視窗 (Content View)
- 外圍邊距：`Margin="28,24,28,28"`。
- 視圖標題列：
  ```xaml
  <StackPanel Orientation="Horizontal" Margin="0,0,0,24">
      <Border Width="4" Height="24" Background="{DynamicResource PrimaryBrush}" CornerRadius="2" Margin="0,0,12,0"/>
      <TextBlock Text="..." FontSize="22" FontWeight="SemiBold"
                 Foreground="{DynamicResource PrimaryTextBrush}" VerticalAlignment="Center"/>
  </StackPanel>
  ```
- 卡片元件 (`hc:Card`)：
  - 邊框：`BorderThickness="1" BorderBrush="{DynamicResource BorderBrush}" Margin="0,0,0,24"`
  - 卡片標頭橫幅：`Background="{DynamicResource PrimaryBrush}" CornerRadius="4,4,0,0" Padding="18,12"`，白字 SemiBold 14px，前置圖示。
  - 卡片內容區域：`Padding="20,18,20,20"`。

## 3. 按鈕與互動元件規範
- 主操作按鈕（執行備份、執行還原、重新偵測）：`Style="{StaticResource ButtonPrimary}"`，高 32px ~ 36px。
- 次要操作按鈕（瀏覽、複製、清空、進階設定）：`Style="{StaticResource ButtonDefault}"`。
- 危險操作（刪除設定檔、中止操作）：`Style="{StaticResource ButtonDanger}"`。
- 輸入框：全面採用 `hc:TextBox`，包含 `hc:InfoElement.Title` 與 `hc:InfoElement.Placeholder`。

## 4. 在地化規範
- 所有文字不可寫死於 XAML，必須透過 `{l:Loc StringKey}` 引用自資源檔。
- 預設語系為繁體中文 (`zh-TW`)，兼具英文 (`en-US`)。
