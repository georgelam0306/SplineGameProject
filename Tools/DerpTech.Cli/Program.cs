using System.CommandLine;
using DerpTech.Cli.Commands;

// Root command
var rootCommand = new RootCommand("DerpTech CLI - Game project scaffolding tool");

// new-game command
var newGameCommand = new Command("new-game", "Create a new game project from BaseTemplate");

var projectNameArg = new Argument<string>(
    "name",
    "Name for the new game project (alphanumeric, starts with letter)");

var descriptionOpt = new Option<string>(
    ["--description", "-d"],
    () => "",
    "Project description");

var authorOpt = new Option<string>(
    ["--author", "-a"],
    () => "",
    "Project author");

var forceOpt = new Option<bool>(
    ["--force", "-f"],
    () => false,
    "Overwrite existing project if it exists");

newGameCommand.AddArgument(projectNameArg);
newGameCommand.AddOption(descriptionOpt);
newGameCommand.AddOption(authorOpt);
newGameCommand.AddOption(forceOpt);

newGameCommand.SetHandler(
    NewGameCommand.ExecuteAsync,
    projectNameArg,
    descriptionOpt,
    authorOpt,
    forceOpt);

rootCommand.AddCommand(newGameCommand);

// Run
return await rootCommand.InvokeAsync(args);
