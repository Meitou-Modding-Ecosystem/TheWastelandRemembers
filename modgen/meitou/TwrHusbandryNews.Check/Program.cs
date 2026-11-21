using System.Reflection;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using TwrHusbandryNews;

if (args is ["--write-mod", var output])
{
    FcsWriter.WriteFile(new FcsFile { Author = "The Wasteland Remembers", Description = "Optional Beastmaster road news" }, output);
    return;
}
if (args.Length > 1) throw new ArgumentException("Pass the Workshop LivingWasteland.mod, or use --write-mod <path>.");

const string Trader = "3-Husbandry.mod", Conversation = "5-Husbandry.mod", Greeting = "6-Husbandry.mod";
const string Buy = "7-Husbandry.mod", Other = "9-Husbandry.mod";
const string Question = "1-" + HusbandryNews.DataFile, Answer = "2-" + HusbandryNews.DataFile, Condition = "3-" + HusbandryNews.DataFile;

CheckAbsent();
CheckReplaced();
var db = Database();
var records = Edit(db);
Require(records.Count == 4, "The expected three additions and one edit were queued.");
db.Apply(new FcsFile().With(records), HusbandryNews.DataFile);
Require(db.Find(Greeting)!.GetReferences("lines").Select(link => link.TargetStringId).SequenceEqual([Buy, Other, Question]),
    "Both existing purchase and conversation choices stay in order.");
Require(db.Find(Answer)!.GetReferences("lines").Select(link => link.TargetStringId).SequenceEqual([Buy, Other]),
    "The news answer returns to the original choices.");
Require(db.Find(Question)!.GetReferences("conditions").Single().TargetStringId == Condition &&
    db.Find(Condition)!.GetInt("condition name") == 29350, "The TWR slot must be available before offering the reply.");
Require(Edit(db).Count == 0, "Running the edit again is a no-op.");
Console.WriteLine("PASS: absent and replaced targets, first application, existing choices, local-news gate, and duplicate prevention");

void CheckAbsent()
{
    Require(Edit(new GameDatabase()).Count == 0, "An absent mod must not receive edits.");
    var missingNews = Database(includeTwr: false);
    Require(Edit(missingNews).Count == 0, "An absent TWR news slot must not receive edits.");
}

void CheckReplaced()
{
    var replaced = Database(replaced: true);
    Require(Edit(replaced).Count == 0, "A greeting whose purchase choice was replaced must not receive edits.");
}

GameDatabase Database(bool replaced = false, bool includeTwr = true)
{
    static FcsRecord Record(string id, FcsRecordType type) => new() { StringId = id, Name = id, RecordType = type };
    var trader = Record(Trader, FcsRecordType.Character);
    trader.References["dialogue"] = [new(Conversation, 0, 0, 0)];
    var conversation = Record(Conversation, FcsRecordType.Dialogue);
    conversation.References["lines"] = [new(Greeting, 0, 0, 0)];
    var greeting = Record(Greeting, FcsRecordType.DialogueLine);
    greeting.Ints["speaker"] = 0;
    greeting.References["lines"] = replaced ? [new(Other, 0, 0, 0)] : [new(Buy, 0, 0, 0), new(Other, 0, 0, 0)];
    var buy = Record(Buy, FcsRecordType.DialogueLine);
    buy.Ints["speaker"] = 1;
    var db = new GameDatabase();
    db.Apply(new FcsFile().With([trader, conversation, greeting, buy, Record(Other, FcsRecordType.DialogueLine)]), "Husbandry.mod");
    if (includeTwr)
    {
        var condition = Record("100-LivingWasteland.mod", FcsRecordType.DialogAction);
        condition.Ints["condition name"] = 29350;
        db.Apply(args.Length == 1 ? FcsReader.ReadFile(args[0]) : new FcsFile().With([condition]), "LivingWasteland.mod");
    }
    return db;
}

static IReadOnlyList<FcsRecord> Edit(GameDatabase db)
{
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var ctor = typeof(ModDataContext).GetConstructors(flags).Single();
    var info = new ModInfo("TwrHusbandryNews", HusbandryNews.DataFile, default);
    var manifest = new CodeModManifest("TwrHusbandryNews", 2, ["TwrHusbandryNews.dll"], typeof(HusbandryNews).FullName!);
    var loaded = ctor.GetParameters()[0].ParameterType.GetConstructors(flags).Single().Invoke([info, manifest]);
    var context = (ModDataContext)ctor.Invoke([loaded, db]);
    new HusbandryNews().EditData(context);
    return (IReadOnlyList<FcsRecord>)typeof(ModDataContext).GetProperty("Changes", flags)!.GetValue(context)!;
}

static void Require(bool test, string message)
{
    if (!test) throw new InvalidOperationException(message);
}

static class TestFiles
{
    internal static FcsFile With(this FcsFile file, IEnumerable<FcsRecord> records)
    {
        file.Records.AddRange(records);
        return file;
    }
}
