#!/usr/bin/env bash
# Runs Pinta.Effects.Tests as several concurrent `dotnet test` processes instead of one
# serial run. Each real image-render test (Utilities.TestEffect) takes real CPU time, and
# NUnit's own [Parallelizable] worker threads don't overlap that work in this project (only
# one worker thread was ever observed busy) - separate OS processes do, since each gets its
# own GTK/Cairo static state instead of contending on whatever in-process lock is
# serializing them. Cuts wall time roughly to the size of the biggest group instead of the
# sum of all of them.
#
# Categories mostly mirror the EffectsTest.<Category>.cs file split, except Render's own
# effects (Cells, Clouds, JuliaFractal, Mandelbrot, Voronoi) get one category each - those
# are the slow, per-pixel procedural generators, so lumping them under one "Render" category
# just recreates a single ~100s group that dominates the whole run. Adjustments and
# ModifierNodeInstantiationTest are already their own fixture classes.
set -u

cd "$(dirname "$0")/.."

CATEGORIES=(Artistic Blurs Cells Clouds Color Distort JuliaFractal Mandelbrot Noise Object Photo Stylize Voronoi)
CLASS_FILTERS=(AdjustmentsTest ModifierNodeInstantiationTest)

# Build once up front: N concurrent `dotnet test` calls against the same project all try to
# take MSBuild's build lock on the same obj/bin output, which serializes them far worse than
# running the tests themselves ever would. --no-build below then skips that check entirely.
dotnet build tests/Pinta.Effects.Tests || exit 1

log_dir=$(mktemp -d)
pids=()
names=()

for cat in "${CATEGORIES[@]}"; do
	dotnet test tests/Pinta.Effects.Tests --no-build --filter "TestCategory=$cat" \
		> "$log_dir/$cat.log" 2>&1 &
	pids+=($!)
	names+=("$cat")
done

for cls in "${CLASS_FILTERS[@]}"; do
	dotnet test tests/Pinta.Effects.Tests --no-build --filter "FullyQualifiedName~$cls" \
		> "$log_dir/$cls.log" 2>&1 &
	pids+=($!)
	names+=("$cls")
done

status=0
for i in "${!pids[@]}"; do
	if ! wait "${pids[$i]}"; then
		status=1
		echo "=== ${names[$i]} FAILED ==="
		cat "$log_dir/${names[$i]}.log"
	else
		tail -n1 "$log_dir/${names[$i]}.log" | sed "s/^/[${names[$i]}] /"
	fi
done

rm -rf "$log_dir"
exit $status
