namespace BZ.Howly.Api.Core.Processes;

/// <summary>Who a step is waiting on. It answers "where is my process stopped?" at a glance.</summary>
public enum StepOwner
{
    /// <summary>The customer must act (send documents, sign, approve).</summary>
    Customer,

    /// <summary>The company team is working on it.</summary>
    Staff,

    /// <summary>A public agency is analyzing it (IBAMA, CETESB, SINIR…).</summary>
    Agency,

    /// <summary>A fee or payment slip must be paid.</summary>
    Payment,
}
