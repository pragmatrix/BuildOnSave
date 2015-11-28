# LiveBuild

## Introduction 

Background compiliation for Visual Studio ... actually, this is an extension for Visual Studio that builds the current solution as soon a file is saved, but in combination with wonderful extension [SaveAllTheTime](https://visualstudiogallery.msdn.microsoft.com/ee676c7f-83e8-4ef8-87ab-22a95ae8f1d4), LiveBuild enables a live, background build while you type.

Notes:

- Barely tested, experimental, so far a one day hack and my first VS extension.
- F# code will be removed soon, this was just an experiment to test if F# code can be used in VS extensions.
- [Serilog](http://serilog.net/) & [Seq](https://getseq.net/) are used for debugging.
- There is a menu named LiveBuild in the Visual Studio menu bar. Right now it does nothing :)

## License

BSD

