// -----------------------------------------------------------------------
//  <copyright file="PrinterActor.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace SeedNode;

public sealed class PrinterActor : UntypedActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    
    protected override void OnReceive(object message)
    {
        _log.Info("Received {0}", message);
    }
}