from pathlib import Path

from core.utils import ensure_directory, write_json


class MetricsCollector:
    def __init__(self, output_dir: Path) -> None:
        self.output_dir = output_dir
        ensure_directory(output_dir)
        self.metrics: dict[str, float | int] = {}

    def record(self, name: str, value: float | int) -> None:
        self.metrics[name] = value

    def write(self) -> None:
        json_path = self.output_dir / "metrics.json"
        prom_path = self.output_dir / "metrics.prom"
        write_json(json_path, self.metrics)
        lines = []
        for key, value in self.metrics.items():
            safe_key = key.replace(".", "_")
            lines.append(f"# TYPE {safe_key} gauge")
            lines.append(f"{safe_key} {value}")
        prom_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
