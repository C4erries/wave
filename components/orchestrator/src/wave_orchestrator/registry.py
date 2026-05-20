from __future__ import annotations

SOURCE_TYPES: dict[str, str] = {
    "synth": "wave-gen",
    "osc": "wave-osc",
}

OPERATOR_TYPES: dict[str, str] = {
    "fft": "wave-fft",
}


def build_command(node_type: str, node_kind: str, params: dict, broker: str) -> list[str]:
    """Assemble a Popen command list for a source or operator node.

    Param keys use underscores (e.g. input_topic) and are converted to CLI
    flags with hyphens (--input-topic).
    """
    if node_kind == "source":
        lookup = SOURCE_TYPES
    elif node_kind == "operator":
        lookup = OPERATOR_TYPES
    else:
        raise ValueError(f"Unknown node_kind '{node_kind}'. Expected 'source' or 'operator'.")

    if node_type not in lookup:
        available = ", ".join(sorted(lookup.keys()))
        raise ValueError(
            f"Unknown {node_kind} type '{node_type}'. Available: {available}"
        )

    cmd: list[str] = [lookup[node_type]]
    for key, val in params.items():
        flag = "--" + key.replace("_", "-")
        cmd.append(flag)
        cmd.append(str(val))
    cmd.extend(["--broker", broker])
    return cmd
