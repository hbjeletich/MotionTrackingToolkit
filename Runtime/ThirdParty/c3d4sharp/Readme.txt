Vendored from https://github.com/lomelina/c3d4sharp (MIT license, see LICENSE.txt),
commit as of 2026-07. Used by MotionRecorder.cs to write .c3d files natively instead
of shelling out to a Python script.

Patched from upstream:
- C3dWriter.WriteFloatFrame() previously discarded the residual/W component of each
  point (always wrote a literal 0, left as "// TODO" in the original). It now writes
  data[i].W, since MotionRecorder needs to mark a joint as missing for a given frame
  (residual < 0, per the C3D spec) when the active tracking source doesn't map it.

Not patched, but important to know when calling this from MotionRecorder.cs:
- C3dWriter's default POINT:SCALE is positive (+1), which per the C3D spec means
  "data is stored as scaled 16-bit integers" — correct for WriteIntFrame, but wrong
  for WriteFloatFrame. Callers using WriteFloatFrame must explicitly set
  writer.Header.ScaleFactor = -1f (and SetParameter<float>("POINT:SCALE", -1f) to
  keep the parameter consistent) before Open(), or compliant C3D readers will
  misinterpret the float data as integers.
- The analogChannelNames constructor parameter defaults to null but is dereferenced
  (.Length) without a null check — pass an empty array, not null, when there's no
  analog data.
