using LenovoDesktopFanControl.ViewModels;

namespace LenovoDesktopFanControl.Tests;

public class RelayCommandTests
{
    [Fact]
    public void Execute_InvokesActionAndIgnoresParameter()
    {
        var executions = 0;
        var command = new RelayCommand(() => executions++);

        command.Execute(new object());

        Assert.Equal(1, executions);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_UsesPredicateEachTime()
    {
        var allowed = false;
        var command = new RelayCommand(() => { }, () => allowed);

        Assert.False(command.CanExecute(null));
        allowed = true;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void GenericCommand_PassesTypedAndNullParametersToDelegates()
    {
        string? executed = "unchanged";
        var command = new RelayCommand<string>(value => executed = value, value => value?.Length == 3);

        Assert.True(command.CanExecute("fan"));
        Assert.False(command.CanExecute("fans"));
        Assert.False(command.CanExecute(null));
        command.Execute("cpu");
        Assert.Equal("cpu", executed);
        command.Execute(null);
        Assert.Null(executed);
    }
}