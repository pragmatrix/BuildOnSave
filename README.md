# BuildOnSave

Put your CPU's cores to use and minimize the time to wait for your build!

[![Build status](https://ci.appveyor.com/api/projects/status/4opfv6rmmw9mlums?svg=true)](https://ci.appveyor.com/project/pragmatrix/buildonsave)

## Introduction 

BuildOnSave is an extension for Visual Studio 2017 that builds the current solution as soon a file is saved, and in combination with the extension [SaveAllTheTime](https://github.com/pragmatrix/SaveAllTheTime/releases), enables a live, background build experience while you type.

### Features

- There is a menu named BuildOnSave in the Visual Studio menu bar with options to disable BuildOnSave and to specify what excactly should be built. Options are stored per solution.
- An output "pane" is registered for BuildOnSave and activates as soon a build starts. 
- An icon is added to the Standard toolbar that shows the build status for background builds. **red** for build failed, **yellow** to show that the build is indeterminate, and **green** means ready to go. If the icon shows an outline instead of a filled circle, a build is currently active. Clicking the icon opens the BuildOnSave output pane.

### Options

These options define what happens as soon one file is saved.

- **Build Solution**  
  Rebuilds the complete solution.
- **Build Startup Project**  
  Rebuilds the startup project.


## Download & Installation

At the [Visual Studio Extension Gallery](https://marketplace.visualstudio.com/items?itemName=pragmatrix.BuildOnSave), or via the [Releases tab](https://github.com/pragmatrix/BuildOnSave/releases) here on Github where you can download the `BuildOnSave.vsix` file and double click it to install it.

## Development & Contribution

Clone the repository and open the solution file `BuildOnSave.sln` in Visual Studio 2017. BuildOnSave should compile out of the box.

To start the Experimental Instance of Visual Studio for a debug session (F5 or Ctrl+F5), add the following to the Debug tab of BuildOnSave's project properties:

- Start external program:  
  `C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\IDE\devenv.exe`
- Command line arguments:  
  `/rootsuffix Exp`

For developing BuildOnSave, please use tabs for indenting.

Debug information is written to a local Seq server. You need to install [Seq] to take a look at the logs.

[Seq]: https://getseq.net/

## License

Copyright (c) 2018, Armin Sander  
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
