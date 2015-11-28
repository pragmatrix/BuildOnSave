# LiveBuild

## Introduction 

Background compilation for Visual Studio ... actually, this is an extension for Visual Studio that builds the current solution as soon a file is saved, but in combination with wonderful extension [SaveAllTheTime](https://visualstudiogallery.msdn.microsoft.com/ee676c7f-83e8-4ef8-87ab-22a95ae8f1d4), LiveBuild enables a live, background build while you type.

Notes:

- Barely tested with Visual Studio 2015 only, highly experimental, a one day hack, and my first VS extension.
- F# code will be removed soon, this was just an experiment to test if F# code can be used in VS extensions.
- [Serilog](http://serilog.net/) & [Seq](https://getseq.net/) are used for debugging.
- There is a menu named LiveBuild in the Visual Studio menu bar. Right now it does nothing :)

## Download & Installation

[Releases are here](https://github.com/pragmatrix/LiveBuild/releases). Download the LiveBuild.vsix file and double click it to install it into Visual Studio.

## License

BSD

