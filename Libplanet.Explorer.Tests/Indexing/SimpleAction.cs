using Bencodex.Types;
using Libplanet.Action;

namespace Libplanet.Explorer.Tests.Indexing;

public class SimpleAction : IAction
{
    public IValue PlainValue => Null.Value;

    public string TypeId => ActionTypeAttribute.ValueOf(GetType());

    public void LoadPlainValue(IValue plainValue)
    {
    }

    public IAccountStateDelta Execute(IActionContext context) => context.PreviousStates;

    public static SimpleAction GetAction(int seed) =>
        (seed % 10) switch
        {
            1 => new SimpleAction1(),
            2 => new SimpleAction2(),
            3 => new SimpleAction3(),
            4 => new SimpleAction4(),
            5 => new SimpleAction5(),
            6 => new SimpleAction6(),
            7 => new SimpleAction7(),
            8 => new SimpleAction8(),
            9 => new SimpleAction10(),
            _ => new SimpleAction0(),
        };
}

public class SimpleAction0 : SimpleAction
{
}

[ActionType(nameof(SimpleAction1))]
public class SimpleAction1 : SimpleAction
{
}

[ActionType(nameof(SimpleAction2))]
public class SimpleAction2 : SimpleAction
{
}

[ActionType(nameof(SimpleAction3))]
public class SimpleAction3 : SimpleAction
{
}

[ActionType(nameof(SimpleAction4))]
public class SimpleAction4 : SimpleAction
{
}

[ActionType(nameof(SimpleAction5))]
public class SimpleAction5 : SimpleAction
{
}

[ActionType(nameof(SimpleAction6))]
public class SimpleAction6 : SimpleAction
{
}

[ActionType(nameof(SimpleAction7))]
public class SimpleAction7 : SimpleAction
{
}

[ActionType(nameof(SimpleAction8))]
public class SimpleAction8 : SimpleAction
{
}

// For overlapping custom action id test
[ActionType(nameof(SimpleAction10))]
public class SimpleAction10 : SimpleAction
{
}
