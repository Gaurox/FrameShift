"""Regenerate tiny cancellation fixtures; development only, requires onnx.

These graphs contain no learned weights and are never part of the application.
The C# tests load the checked-in files without Python or network access.
"""

from pathlib import Path

import onnx
from onnx import TensorProto as T, helper as h


def make_model(stage: str, busy: bool):
    name = "feat_erb" if stage == "encoder" else "emb"
    inputs = [h.make_tensor_value_info(name, T.FLOAT, [1, 1, "t", 32])]
    if stage == "encoder":
        inputs.append(h.make_tensor_value_info("feat_spec", T.FLOAT, [1, 2, "t", 96]))
    initializers = [
        h.make_tensor("time_index", T.INT64, [], [2]),
        h.make_tensor("limit", T.INT64, [], [20]),
        h.make_tensor("iterations", T.INT64, [], [20_000_000 if busy else 0]),
        h.make_tensor("condition", T.BOOL, [], [True]),
    ]
    nodes = [
        h.make_node("Shape", [name], ["shape"]),
        h.make_node("Gather", ["shape", "time_index"], ["frames"], axis=0),
        h.make_node("Greater", ["frames", "limit"], ["is_long"]),
        h.make_node("Cast", ["is_long"], ["long_int"], to=T.INT64),
        h.make_node("Mul", ["long_int", "iterations"], ["trip_count"]),
    ]
    if stage == "df":
        initializers.extend([
            h.make_tensor("one_shape", T.INT64, [1], [1]),
            h.make_tensor("tail_shape", T.INT64, [2], [96, 10]),
            h.make_tensor("axis", T.INT64, [1], [0]),
        ])
        nodes.extend([
            h.make_node("Unsqueeze", ["frames", "axis"], ["time_shape"]),
            h.make_node("Concat", ["one_shape", "time_shape", "tail_shape"], ["coef_shape"], axis=0),
            h.make_node("ConstantOfShape", ["coef_shape"], ["state"],
                        value=h.make_tensor("zero", T.FLOAT, [1], [0.0])),
        ])
        dimensions, state, output = [1, "t", 96, 10], "state", "coefs"
    else:
        dimensions, state = [1, 1, "t", 32], name
        output = "emb" if stage == "encoder" else "gains"

    # A finite native loop holds one selected stage open until Terminate is set.
    # <=20-frame inputs skip it, allowing reuse of the same sessions after cancel.
    body = h.make_graph([
        h.make_node("Identity", ["cond_in"], ["cond_out"]),
        h.make_node("Identity", ["state_in"], ["state_out"]),
    ], "finite_test_loop", [
        h.make_tensor_value_info("i", T.INT64, []),
        h.make_tensor_value_info("cond_in", T.BOOL, []),
        h.make_tensor_value_info("state_in", T.FLOAT, dimensions),
    ], [
        h.make_tensor_value_info("cond_out", T.BOOL, []),
        h.make_tensor_value_info("state_out", T.FLOAT, dimensions),
    ])
    nodes.append(h.make_node("Loop", ["trip_count", "condition", state], [output], body=body))
    model = h.make_model(h.make_graph(nodes, "FrameShift cancellation fixture", inputs,
        [h.make_tensor_value_info(output, T.FLOAT, dimensions)], initializers),
        opset_imports=[h.make_opsetid("", 13)], ir_version=8)
    onnx.checker.check_model(model)
    return model


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[2] / "tests/FrameShift.Tests/Assets/RemoveNoise"
    root.mkdir(parents=True, exist_ok=True)
    for stage in ("encoder", "erb", "df"):
        for busy in (False, True):
            path = root / f'{stage}{"-busy" if busy else ""}.onnx'
            path.write_bytes(make_model(stage, busy).SerializeToString())
