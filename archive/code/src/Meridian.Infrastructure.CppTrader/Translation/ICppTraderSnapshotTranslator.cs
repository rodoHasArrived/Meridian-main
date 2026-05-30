using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Translation;

public interface ICppTraderSnapshotTranslator
{
    LOBSnapshot Translate(BookSnapshotEvent snapshotEvent);
}
