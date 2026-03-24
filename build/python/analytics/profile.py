from core.utils import colorize


def format_bar(duration_ms: int, max_ms: int) -> str:
    width = 30
    if max_ms == 0:
        return ""
    filled = int((duration_ms / max_ms) * width)
    return "█" * filled + "░" * (width - filled)


def print_profile(phase_durations: dict[str, int]) -> None:
    if not phase_durations:
        print("No phase data available.")
        return
    max_ms = max(phase_durations.values())
    print(colorize("Build Profile", "0;36"))
    print(colorize("=" * 40, "0;36"))
    for phase, duration in phase_durations.items():
        bar = format_bar(duration, max_ms)
        print(f"{phase:<12} {duration:>6}ms {bar}")
