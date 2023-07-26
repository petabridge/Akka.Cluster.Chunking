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
        if(message is byte[] bytes)
            _log.Info("Received [{0}] bytes from [{1}]", bytes.Length, Sender);
        else
        {
            _log.Info("Received [{0}] from [{1}]", message, Sender);
        }
    }
}