using Xunit;

// "Démarrage" lists the scheduled tasks that start a program at sign-in, never Windows' own.
public sealed class StartupTasksTests
{
    const int Logon = 9, Daily = 2;

    [Fact]
    public void Lists_a_program_started_at_sign_in() =>
        Assert.True(StartupTasks.Listed(@"\Adobe Acrobat Update Task", [(Logon, true)], runsProgram: true));

    [Fact]
    public void Leaves_out_windows_own_tasks() =>
        Assert.False(StartupTasks.Listed(@"\Microsoft\Windows\Maps\MapsToastTask", [(Logon, true)], runsProgram: true));

    [Fact]
    public void Leaves_out_tasks_that_do_not_start_at_sign_in()
    {
        Assert.False(StartupTasks.Listed(@"\GoogleUpdateTaskMachineCore", [(Daily, true)], runsProgram: true));
        Assert.False(StartupTasks.Listed(@"\Vendor\Updater", [(Logon, false)], runsProgram: true)); // trigger turned off
        Assert.False(StartupTasks.Listed(@"\Vendor\Mailer", [(Logon, true)], runsProgram: false)); // no program to run
    }

    [Theory]
    [InlineData(@"\Vendor\Updater", "on", true)]
    [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag", "off", false)] // Windows' own: never
    [InlineData(@"\Microsoft", "off", false)]
    [InlineData("Vendor Updater", "off", false)] // not a task path
    public void The_administrator_helper_only_switches_listed_kinds_of_task(string path, string state, bool accepted)
    {
        string[] args = [AdminCommand.StartupSet, "task", path, state];
        if (accepted) Assert.Equal(path, AdminCommand.Parse(args).Args[1]);
        else Assert.Throws<ArgumentException>(() => AdminCommand.Parse(args));
    }
}
