using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace TwrHusbandryNews;

public sealed class HusbandryNews : IMod, IModDataPhase
{
    public const string DataFile = "TwrHusbandryNews.mod";
    const string Trader = "3-Husbandry.mod", Conversation = "5-Husbandry.mod", Greeting = "6-Husbandry.mod";
    const string Buy = "7-Husbandry.mod", Question = "1-" + DataFile, Answer = "2-" + DataFile, Condition = "3-" + DataFile;
    const int NewsSlot = 29350;

    public void Initialize(ModContext context) { }

    public void EditData(ModDataContext context)
    {
        if (context.Data.Find(Trader) is not { Type: FcsRecordType.Character } trader ||
            !trader.GetReferences("dialogue").Any(link => link.TargetStringId == Conversation) ||
            context.Data.Find(Conversation) is not { Type: FcsRecordType.Dialogue } conversation ||
            !conversation.GetReferences("lines").Any(link => link.TargetStringId == Greeting) ||
            context.Data.Find(Greeting) is not { Type: FcsRecordType.DialogueLine } greeting ||
            greeting.GetInt("speaker") != 0 ||
            !greeting.GetReferences("lines").Any(link => link.TargetStringId == Buy) ||
            context.Data.Find(Buy) is not { Type: FcsRecordType.DialogueLine } sale || sale.GetInt("speaker") != 1 ||
            new[] { Question, Answer, Condition }.Any(id => context.Data.Find(id) is not null) ||
            !context.Data.OfType(FcsRecordType.DialogAction).Any(record => record.GetInt("condition name") == NewsSlot &&
                record.StringId.EndsWith("-LivingWasteland.mod", StringComparison.Ordinal)))
            return;

        var choices = greeting.GetReferences("lines").Select(link =>
            new FcsReference(link.TargetStringId, link.Values.Value0, link.Values.Value1, link.Values.Value2)).ToList();
        var condition = context.Add(Condition, FcsRecordType.DialogAction, "TWR local news is available");
        condition.Ints["condition name"] = NewsSlot;
        condition.Ints["compare by"] = 0;
        condition.Ints["who"] = 0;

        var question = context.Add(Question, FcsRecordType.DialogueLine, "DIALOGUE_LINE");
        Line(question, 1, "Any news from the roads?");
        question.References["conditions"] = [new(Condition, 1, 0, 0)];
        question.References["lines"] = [new(Answer, 0, 0, 0)];

        var answer = context.Add(Answer, FcsRecordType.DialogueLine, "DIALOGUE_LINE");
        Line(answer, 0, "/LWNEWS_0/");
        answer.References["conditions"] = [new(Condition, 1, 0, 0)];
        answer.References["lines"] = choices;

        // Data-phase reference entries merge by target. Existing choices and their order stay in the greeting.
        context.Edit(Greeting).References["lines"] = [new(Question, 0, 0, 0)];
        context.Log("Added the local road-news option to the Hive Beastmaster.");
    }

    static void Line(FcsRecord line, int speaker, string text)
    {
        line.Ints["speaker"] = speaker;
        line.Floats["chance permanent"] = 100;
        line.Floats["chance temporary"] = 100;
        line.Strings["text0"] = text;
    }
}
