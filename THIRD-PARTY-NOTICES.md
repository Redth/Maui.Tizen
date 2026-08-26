# Third-Party Notices

This repository contains code originally authored in the [dotnet/maui](https://github.com/dotnet/maui)
repository, and depends on components published by Samsung. This file records the
licensing position for both.

In the event that a required notice is missing, please open an issue.

---

## Code originating from dotnet/maui

The .NET MAUI Tizen backend sources in this repository were extracted from
`dotnet/maui` with their original commit history and authorship intact. See
[`PROVENANCE.md`](PROVENANCE.md) for the exact commits, the extraction method, and
the pull requests that introduced the backend upstream.

`dotnet/maui` is distributed under the MIT License:

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The upstream `LICENSE.txt` and `THIRD-PARTY-NOTICES.TXT` files are deliberately
retained inside the imported history so that the licensing lineage is verifiable from
the git log itself, not just from this file.

---

## Samsung Tizen dependencies (referenced, not redistributed)

> **No Samsung source code is copied into this repository.** The components below are
> consumed exclusively as published NuGet packages and .NET SDK workload packs. They are
> listed here because building and running this backend requires them, and because their
> licence differs from this repository's MIT licence.

### TizenFX

- **Project:** [Samsung/TizenFX](https://github.com/Samsung/TizenFX)
- **Licence:** Apache License 2.0
- **How it is consumed:** as reference assemblies delivered by the
  `samsung.net.sdk.tizen` .NET SDK workload (`Samsung.Tizen.Ref.API*` packs) and the
  `Samsung.Tizen.Sdk` pack.
- **Redistributed here:** no.

### Tizen.UIExtensions

- **Project:** [Samsung/Tizen.UIExtensions](https://github.com/Samsung/Tizen.UIExtensions)
- **Licence:** Apache License 2.0
- **How it is consumed:** as the `Tizen.UIExtensions.NUI` NuGet package reference, which
  the Tizen backend's handler implementations build against. This mirrors upstream
  `dotnet/maui`, where `src/Core/src/Core.csproj` carries the same `PackageReference`.
- **Redistributed here:** no.

### Apache License 2.0 notice

Both components above are licensed under the Apache License, Version 2.0. You may obtain
a copy of the License at:

```
http://www.apache.org/licenses/LICENSE-2.0
```

Unless required by applicable law or agreed to in writing, software distributed under the
License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND,
either express or implied. See the License for the specific language governing permissions
and limitations under the License.

The full Apache-2.0 text is not reproduced here because no Apache-2.0-licensed source is
distributed by this repository; the dependencies carry their own licence files inside
their respective NuGet packages.
