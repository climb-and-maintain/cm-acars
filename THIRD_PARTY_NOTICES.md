# Third-party notices

This notice covers the production dependencies and adapted data distributed with Climb and Maintain ACARS. Managed package versions are recorded in `packages.lock.json`, and the self-contained runtime version is pinned in `Directory.Build.props`; test-only packages are not shipped in the installer. Every self-contained publish also carries the exact .NET runtime notices as `DOTNET_RUNTIME_THIRD_PARTY_NOTICES.txt`, plus the .NET and Windows Desktop runtime license files from the restored runtime packs.

## MIT-licensed components

- System.Waf.Core 8.3.0 and System.Waf.Wpf 8.3.0 — Copyright © 2016–2026 jbe2277 — <https://github.com/jbe2277/waf>
- Microsoft .NET runtime and Microsoft.Extensions components — Copyright © Microsoft Corporation and .NET Foundation contributors — <https://github.com/dotnet/runtime>
- Microsoft.Data.Sqlite 10.0.11 — Copyright © Microsoft Corporation — <https://github.com/dotnet/efcore>

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## NLog 6.2.0 — BSD 3-Clause

Copyright (c) 2004–2026 NLog Project — <https://nlog-project.org/>

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## BSD 2-Clause components and data

- NLog.Extensions.Logging 6.2.0 — Copyright (c) 2004–2026 NLog Project — <https://github.com/NLog/NLog.Extensions.Logging>
- Adapted Fenix and PMDG aircraft-profile data from phpVMS `acars-config` — Copyright 2024 vmslabs; Fenix original profile author B.Fatih KOZ and PMDG profile author `acars` — <https://github.com/phpvms/acars-config>

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The bundled Fenix and PMDG profiles identify upstream revision `20a81525037fcf920f158b4c063b48f33734a502` and their exact source paths in metadata. Climb and Maintain changes do not erase that attribution.

## SQLitePCLRaw 2.1.12 — Apache License 2.0

SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core, SQLitePCLRaw.lib.e_sqlite3, and SQLitePCLRaw.provider.e_sqlite3 are Copyright 2014–2024 SourceGear, LLC and are licensed under Apache-2.0. The complete Apache License 2.0 text is reproduced in `LICENSE.txt` installed with this application.

The SQLite library itself is dedicated to the public domain. See <https://www.sqlite.org/copyright.html>.

## NSIS installer — zlib/libpng license

NSIS is used to produce the Windows installer and may contribute installer runtime code. Copyright belongs to the NSIS contributors. <https://nsis.sourceforge.io/License>

This software is provided "as-is", without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.

Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to these restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.

## Microsoft SimConnect exclusion

Microsoft SimConnect binaries are not distributed components of this project. The repository, installer, and application download mechanism contain neither `SimConnect.dll` nor `Microsoft.FlightSimulator.SimConnect.dll`. The application contains original C# interop declarations and lets the user select a compatible native library obtained independently from a Microsoft-provided source they are entitled to use.

Do not add Microsoft SimConnect DLLs to this notice as bundled material. Project policy prohibits bundling or downloading them.

## Aircraft developers

Fenix Simulations and PMDG software, aircraft files, libraries, and proprietary documentation are not distributed. Profiles contain only attributed configuration or independently authored read-only mappings. Climb and Maintain ACARS is independent and is not affiliated with or endorsed by those developers.
