# Built-in archive extractor: third-party notices

The launcher bundles these managed assemblies inside its executable and loads them from the executable at runtime. They are used to unpack downloaded game-client archives; no 7-Zip, WinRAR, or other separately installed extractor is launched.

## SharpCompress 0.50.7

Project: <https://github.com/adamhathcock/sharpcompress>
Pinned source commit: `b4af8afe6683d019d504954a3b4e4f18b06e2cd6`
NuGet package: <https://www.nuget.org/packages/SharpCompress/0.50.7>
License: MIT. Copyright (c) 2014 Adam Hathcock.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

SharpCompress's upstream README also includes a Bouncy Castle MIT notice for code credited there. That notice is retained here:

Copyright (c) 2000 - 2011 The Legion Of The Bouncy Castle (http://www.bouncycastle.org)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Microsoft .NET dependencies

These package assemblies are bundled because SharpCompress targets .NET Framework 4.8 and declares them as dependencies. Microsoft packages and source:

- `Microsoft.Bcl.AsyncInterfaces` 8.0.0 — <https://www.nuget.org/packages/Microsoft.Bcl.AsyncInterfaces/8.0.0>
- `System.Text.Encoding.CodePages` 8.0.0 — <https://www.nuget.org/packages/System.Text.Encoding.CodePages/8.0.0>
- `System.Threading.Tasks.Extensions` 4.5.4 — <https://www.nuget.org/packages/System.Threading.Tasks.Extensions/4.5.4>
- `System.Memory` 4.5.5 — <https://www.nuget.org/packages/System.Memory/4.5.5>
- `System.Runtime.CompilerServices.Unsafe` 6.0.0 — <https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.0.0>
- `System.Buffers` 4.5.1 — <https://www.nuget.org/packages/System.Buffers/4.5.1>
- `System.Numerics.Vectors` 4.5.0 — <https://www.nuget.org/packages/System.Numerics.Vectors/4.5.0>

The Microsoft components are distributed under the MIT License. Copyright (c) .NET Foundation and contributors. See <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>.
