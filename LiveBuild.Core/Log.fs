namespace LiveBuild.Core

open Serilog
open Serilog.Events

[<AbstractClass;Sealed>]
type Log() =

    static let log_:ILogger = 
        LoggerConfiguration()
#if DEBUG
            .Destructure.FSharpTypes()
            .WriteTo.Seq("http://localhost:5341")    
            .MinimumLevel.Debug()
#endif
            .CreateLogger()
    
    static member log = log_

    static member V (template, [<System.ParamArray>] values) =
        Log.log.Write(LogEventLevel.Verbose, template, values)
    static member D (template, [<System.ParamArray>] values) =
        Log.log.Write(LogEventLevel.Debug, template, values)
    static member I (template, [<System.ParamArray>] values) =
        Log.log.Write(LogEventLevel.Information, template, values)
    static member W (template, [<System.ParamArray>] values) =
        Log.log.Write(LogEventLevel.Warning, template, values)
    static member E (template, [<System.ParamArray>] values) =
        Log.log.Write(LogEventLevel.Error, template, values)
    static member F (template, [<System.ParamArray>] values) =
        Log.log.Write(LogEventLevel.Fatal, template, values)
    
