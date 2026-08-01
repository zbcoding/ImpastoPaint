# Third-Party Notices

This product, **Impasto** (a fork of [Pinta](https://github.com/PintaProject/Pinta)),
includes third-party components that are redistributed with the application (bundled
in the macOS and Windows installers, or linked from system packages on Linux).

The Impasto application itself is licensed under the MIT License (see
`license-mit.txt`). Portions derived from Paint.NET are licensed separately (see
`license-pdn.txt`).

The components listed below are owned by their respective copyright holders and
are provided under their own licenses, which are reproduced here.

## Icon sets

The application bundles icons from the following sources, reproduced here with
their respective licenses.

### Paint.Net 3.0

Used under the MIT License. Copyright (c) dotPDN LLC, Rick Brewster and
contributors. See also `license-pdn.txt` and
`license-mit.txt` for the license texts.

### Silk icon set

FamFamFam Silk icon set, by Mark James. Used under the
[Creative Commons Attribution 3.0 License](http://creativecommons.org/licenses/by/3.0/).
Attribution: Mark James, https://www.famfamfam.com.

### Fugue icon set

Fugue icon set, by Yusuke Kamiyamane. Used under the
[Creative Commons Attribution 3.0 License](http://creativecommons.org/licenses/by/3.0/).
Attribution: Yusuke Kamiyamane, https://p.yusukekamiyamane.com.

### Pinta contributors

Icons created by Pinta contributors are used under the same license as the
project itself (MIT). See `Pinta.Resources/icons/pinta-icons.md` for the list
of such icons.

## Components and licenses

### LGPL-2.1-or-later (the GTK toolkit stack)

Bundled on macOS and Windows; provided by system packages on Linux.

- GTK 4 (`libgtk-4`)
- libadwaita (`libadwaita-1`)
- GLib, GObject, GIO, GModule (`libglib-2.0`, `libgobject-2.0`, `libgio-2.0`, `libgmodule-2.0`)
- gdk-pixbuf (`libgdk_pixbuf-2.0`)
- Pango, PangoCairo, PangoFT2, PangoWin32 (`libpango-1.0`, `libpangocairo`, `libpangoft2`, `libpangowin32`)
- Cairo (`libcairo-2`) — also available under MPL-1.1
- librsvg (`librsvg-2`)
- libfribidi, libgraphite2, libthai, libdatrie
- libappstream, libxmlb
- libiconv, libintl (gettext)

### BSD-2-Clause

- libavif (`libavif`) — AVIF encoder/decoder. Copyright 2019 Joe Drago
- libaom (`libaom`) — AV1 codec. Copyright (c) 2016, Alliance for Open Media
- libtiff (`libtiff`)

### BSD-3-Clause

- libwebp, libwebpdemux, libwebpmux (`libwebp`) — Copyright (c) 2010, Google Inc.
- libyuv (`libyuv`) — Copyright (c) 2011 The LibYuv Project Authors
- libsharpyuv (`libsharpyuv`)
- libssh2
- libzstd — Copyright (c) Meta Platforms, Inc. and affiliates
- libjpeg-turbo (`libjpeg`)

### MIT / X11

- libxml2, libffi, libpixman, libepoxy, libexpat, libcurl
- libnghttp2, libnghttp3, libngtcp2
- libfyaml, libpsl, libharfbuzz, libwinpthread, libunwind, brotli

### Apache-2.0

- OpenSSL (`libssl`, `libcrypto`)
- liblerc

### Other permissive licenses

- zlib — zlib License
- libpng — PNG Reference Library license
- libdeflate — zlib License
- libbz2 — bzip2 License
- liblzma — public domain (0BSD)
- libjbig — ISC-style

### LGPL-3.0-or-later

- libunistring
- libidn2

## License texts

### BSD-2-Clause License

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

### BSD-3-Clause License

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

### MIT License

```
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

### GNU Lesser General Public License, version 2.1 (LGPL-2.1)

The GTK toolkit stack is licensed under the GNU Lesser General Public License,
version 2.1 or later. The full license text is available at:
https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html

This library is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the GNU Lesser General Public License for more details.

### zlib License

```
This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the
use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software in
   a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

### PNG Reference Library License

```
The PNG Reference Library is copyright (c) 1995-2019 The PNG Reference Library
Authors.

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

### Apache License 2.0

OpenSSL and liblerc are licensed under the Apache License 2.0. The full license
text is available at: https://www.apache.org/licenses/LICENSE-2.0

### LGPL-3.0-or-later

libunistring and libidn2 are licensed under the GNU Lesser General Public
License, version 3 or later. The full license text is available at:
https://www.gnu.org/licenses/lgpl-3.0.html
