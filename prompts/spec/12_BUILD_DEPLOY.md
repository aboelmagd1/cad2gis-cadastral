# CAD2GIS Specification — Document 12: Build, Packaging, and Deployment

> **ملخص باللغة العربية (Arabic Summary):**  
> يحدد هذا المستند متطلبات بناء وتجميع ونشر إضافة CAD2GIS الموجهة لبرنامج ArcGIS Pro 3.3.0 باستخدام .NET 8.0 SDK. يشرح المسار الهيكلي لحزمة التوزيع `.esriAddinX` والملفات المستهدفة داخل مجلد التجميع `Install/` مع الحفاظ على ملف التكوين `Config.daml` والصور في الجذر، ومجلد `HelperFiles/` كنسخة أولية. يلتزم النظام بعدم التثبيت التلقائي الصامت احتراماً لمبدأ الأمان (INV-04)، ويوضح بيئة تصحيح الأخطاء (Debugging) والحدود البيئية بين مكتبة `Cad2Gis.Core` المستقلة ومكتبة الإضافة `Cad2Gis.AddIn`.

---

## 1. Prerequisites & Toolchain

| Component | Target Version | Verification Command / Check | Purpose |
| :--- | :--- | :--- | :--- |
| **Operating System** | Windows 10/11 x64 | `[System.Environment]::OSVersion` | Host environment for ArcGIS Pro |
| **.NET SDK** | .NET 8.0 SDK (8.0.x) | `dotnet --version` (Outputs `8.0.xxx`) | Core compilation & build runner |
| **IDE** | Visual Studio 2022 (v17.8+) or VS Code | Check installed workloads | C# development & debugging |
| **ArcGIS Pro** | ArcGIS Pro 3.3.0 (Build 52636+) | `ArcGISPro.exe` file version | Runtime host application |
| **ArcGIS Pro SDK** | ArcGIS Pro SDK for .NET 3.3 | Check NuGet / Pro bin assemblies | API bindings (`ArcGIS.Core.dll`, etc.) |
| **ArcPy (Optional)** | Python 3.9+ (Pro default env) | `propy -c "import arcpy; print(arcpy.GetInstallInfo())"` | Fallback Python Toolbox execution |

### Solution Structure
```
Cad2Gis/
├── Cad2Gis.sln                     # Visual Studio Solution binding 3 projects
├── Cad2Gis.Core/
│   └── Cad2Gis.Core.csproj         # TargetFramework: net8.0 (No ArcGIS Pro SDK dependencies)
├── Cad2Gis.AddIn/
│   ├── Cad2Gis.AddIn.csproj        # TargetFramework: net8.0-windows, UseWPF: true
│   ├── Config.daml                 # ArcGIS Pro declarative UI declaration
│   └── Images/                     # Toolbar and button ribbon icons (16px, 32px, 64px)
├── Cad2Gis.Tests/
│   └── Cad2Gis.Tests.csproj        # TargetFramework: net8.0 (xUnit + FluentAssertions)
└── package_addin.ps1               # Automated PowerShell release packaging script
```

---

## 2. Compilation and Build Pipeline

### 2.1 Building Standalone Core & Tests (No Pro Required)
The core business logic and computational geometry engine are fully isolated from ArcGIS Pro SDK assemblies:
```powershell
# Run from Cad2Gis/ directory
dotnet build Cad2Gis.Core/Cad2Gis.Core.csproj -c Release
dotnet test Cad2Gis.Tests/Cad2Gis.Tests.csproj -c Release
```
*Acceptance:* All 240+ unit tests must pass with 0 failures before proceeding to Add-in packaging.

### 2.2 Building Full Add-in Solution (Requires ArcGIS Pro 3.3 Installation)
The Add-in project references assemblies located in `C:\Program Files\ArcGIS\Pro\bin\`:
```powershell
dotnet build Cad2Gis.sln -c Debug
# Or Release:
dotnet build Cad2Gis.sln -c Release
```
Output binaries are generated under `Cad2Gis/Cad2Gis.AddIn/bin/Debug/net8.0-windows/`.

---

## 3. Package Staging and `.esriAddinX` Specification

An ArcGIS Pro Add-in package (`.esriAddinX`) is a renamed ZIP archive adhering to strict Esri staging hierarchy rules.

### 3.1 Packaging Layout
```
Cad2Gis.esriAddinX (ZIP Root)
├── Config.daml                     # Declarative configuration at the root
├── Images/                         # Ribbon & UI icon assets
│   ├── Cad2Gis_16.png
│   ├── Cad2Gis_32.png
│   └── ...
├── HelperFiles/                    # Packaged seed templates, lookups & settings
│   ├── settings.json
│   ├── Required Fields in the Shapefile.xlsx
│   ├── lookups/
│   └── templates/
└── Install/                        # MANDATORY subfolder for all executable code
    ├── Cad2Gis.AddIn.dll
    ├── Cad2Gis.Core.dll
    ├── ClosedXML.dll
    ├── DocumentFormat.OpenXml.dll
    ├── ExcelNumberFormat.dll
    ├── SixLabors.Fonts.dll
    ├── HelperFiles/                # Secondary seed for local reflection loading
    └── ... (all dependency assemblies and .pdb debug symbols)
```

> [!CRITICAL]
> All binary DLLs and dependencies **must** reside in the `Install/` subfolder. If placed in the root, ArcGIS Pro will fail to load the Add-in assembly at runtime.

### 3.2 Packaging Script (`package_addin.ps1`)
The packaging workflow is automated by `Cad2Gis/package_addin.ps1`:
1. Executes `dotnet build Cad2Gis.sln -c Debug`.
2. Creates temporary staging directory `Cad2Gis/bin/Package/staging/`.
3. Populates `staging/Install/` with binary output from `Cad2Gis.AddIn/bin/Debug/net8.0-windows/`.
4. Copies `Config.daml` and `Images/` to `staging/` root.
5. Copies `HelperFiles/` seed data to both `staging/HelperFiles/` and `staging/Install/HelperFiles/`.
6. Compresses `staging/` into `Cad2Gis/bin/Package/Cad2Gis.esriAddinX` via `System.IO.Compression.ZipFile`.
7. Copies the resulting `.esriAddinX` to the workspace root for convenient access.
8. Cleans up staging directory.

---

## 4. Deployment and Installation

### 4.1 Manual User Installation (Enforcing INV-04)
To prevent unexpected workspace tampering or privilege escalation, silent auto-installation into the user's roaming directory is strictly forbidden:
1. Locate `Cad2Gis.esriAddinX` in the workspace root or `Cad2Gis/bin/Package/`.
2. Double-click `Cad2Gis.esriAddinX`.
3. The official **Esri ArcGIS Pro Add-in Installation Utility** launches.
4. Verify the digital signature/metadata (Author: *CAD2GIS Development Team*, Version: *1.0.0*).
5. Click **Install Add-In**.
6. The utility extracts the package to:
   `%LOCALAPPDATA%\ESRI\ArcGISPro\AssemblyCache\{6896173a-49ae-44d4-9d51-c06782d41517}\`
7. Launch ArcGIS Pro. The **CAD2GIS** tab appears in the main ribbon.

### 4.2 Uninstallation and Clean-up
To remove or upgrade the Add-in:
1. Open ArcGIS Pro $\rightarrow$ **Settings** $\rightarrow$ **Add-In Manager**.
2. Select **CAD2GIS** $\rightarrow$ Click **Delete this Add-In**.
3. Or manually delete the folder `{6896173a-49ae-44d4-9d51-c06782d41517}` from `%LOCALAPPDATA%\ESRI\ArcGISPro\AssemblyCache\`.

---

## 5. Debugging in ArcGIS Pro

To debug the Add-in within a live ArcGIS Pro session using Visual Studio:
1. Open `Cad2Gis.sln` in Visual Studio 2022.
2. In `Cad2Gis.AddIn` project properties $\rightarrow$ **Debug** $\rightarrow$ **General** $\rightarrow$ **Open debug launch profiles UI**:
   - **Launch:** Executable
   - **Executable:** `C:\Program Files\ArcGIS\Pro\bin\ArcGISPro.exe`
   - **Command line arguments:** Optional path to a test project (e.g., `D:\TestProjects\CadTest.aprx`)
   - **Working directory:** `C:\Program Files\ArcGIS\Pro\bin`
3. Set desired breakpoints in `Cad2GisDockpaneViewModel.cs` or Core engines.
4. Press `F5` (Start Debugging). Visual Studio launches ArcGIS Pro and attaches the .NET 8 debugger.
5. In ArcGIS Pro, open the CAD2GIS dockpane; breakpoints trigger immediately upon user interaction.

---

## 6. Environment Limitations & Isolation Matrix

| Capability | `Cad2Gis.Core` / `Cad2Gis.Tests` | `Cad2Gis.AddIn` | `Cad2Gis_ArcPy.pyt` |
| :--- | :--- | :--- | :--- |
| **Requires ArcGIS Pro Installed** | **NO** | **YES** (SDK assemblies) | **YES** (`arcpy` module) |
| **Requires Pro License Active** | **NO** | **YES** | **YES** |
| **Runs in CI / GitHub Actions** | **YES** (Standard Linux/Windows runner) | NO (Requires Pro SDK) | NO |
| **Geometry Computation Engine** | Custom Pure C# (`GeometryEngine2D`) | Delegates to Core | Uses ArcPy Geoprocessing |
| **File I/O** | Native C# + ClosedXML | Native C# + Pro API | Python + OpenPyXL |

---

## 7. Requirement Verification Traceability

| Requirement ID | Description | Source / Code Anchor | Verification Status |
| :--- | :--- | :--- | :--- |
| `REQ-BLD-001` | Target framework .NET 8.0 for Core, Tests, and AddIn. | `Cad2Gis.*.csproj` | `IMPLEMENTED` |
| `REQ-BLD-002` | Automated `.esriAddinX` packaging via PowerShell with valid Esri folder structure (`Install/`, `Config.daml`, `Images/`). | `package_addin.ps1` | `IMPLEMENTED` |
| `REQ-BLD-003` | Prohibit automatic silent installation; adhere to explicit user installation workflow (INV-04). | `package_addin.ps1:L65` | `IMPLEMENTED` |
| `REQ-BLD-004` | Complete isolation of `Cad2Gis.Core` from `ArcGIS.Desktop.*` and `ArcGIS.Core` SDK references. | `Cad2Gis.Core.csproj` | `IMPLEMENTED` |
| `REQ-BLD-005` | Packaging script automatically bundles `HelperFiles` template seed into distribution archive. | `package_addin.ps1:L40-49` | `IMPLEMENTED` |
