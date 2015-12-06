# BuildOnSave

Never wait for a build anymore!

[![Build status](https://ci.appveyor.com/api/projects/status/4opfv6rmmw9mlums?svg=true)](https://ci.appveyor.com/project/pragmatrix/buildonsave)

## Introduction 

This is an extension for Visual Studio that builds the current solution as soon a file is saved, and in combination with the wonderful extension [SaveAllTheTime](https://visualstudiogallery.msdn.microsoft.com/ee676c7f-83e8-4ef8-87ab-22a95ae8f1d4), enables a live, background build experience while you type.

Notes:

- Runs with Visual Studio 2015 only, barely tested, and highly experimental.
- Some debug information is written to the Windows debug output, which can be monitored with [DebugView](https://technet.microsoft.com/en-us/sysinternals/debugview.aspx).
- There is a menu named BuildOnSave in the Visual Studio menu bar.
- An output "pane" is registered for BuildOnSave and also activated as soon a build starts.
- It's possible to automatically build the whole solution in its active configuration or a single project.

## Download & Installation

[Releases are here](https://github.com/pragmatrix/BuildOnSave/releases). Download the BuildOnSave.vsix file and double click it to install it into Visual Studio.

## Development & Contribution

To start the Experimental Instance of Visual Studio for a debug session (F5 or Ctrl+F5), add the following to the Debug tab of BuildOnSave's project properties:

- Start external program:  
  `C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\devenv.exe`
- Command line arguments:  
  `/rootsuffix Exp`

For developing BuildOnSave, please use tabs and the [EditorConfig](https://visualstudiogallery.msdn.microsoft.com/c8bccfe2-650c-4b42-bc5c-845e21f96328) extension.

## License

Copyright (c) 2015, Armin Sander  
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
