#!/usr/bin/env bash
# Runs the whole suite (Core, Tools, Effects) as concurrent `dotnet test`
# processes so wall time is bounded by the slowest single group instead of the
# sum of them. Pinta.Effects.Tests is subdivided into category groups - see
# scripts/test-effects-parallel.sh for why (NUnit workers don't overlap the
# real per-pixel render work; separate OS processes are needed to overlap it).
#
# All selected projects build once up front, then run with --no-build:
# concurrent `dotnet test` calls against the same project otherwise serialize
# on MSBuild's build lock, which is far worse than any of the tests.
#
# Usage: scripts/test-all-parallel.sh [--only GROUP[,GROUP...]]
#   --only restricts to named groups (Core, Tools, Render-<Category>,
#   AdjustmentsTest, ModifierNodeInstantiationTest). Case-insensitive substring
#   match, e.g. --only tools,render-voronoi.
set -u

cd "$(dirname "$0")/.."

EFFECTS_CATEGORIES=(Artistic Blurs Cells Clouds Color Distort JuliaFractal Mandelbrot Noise Object Photo Stylize Voronoi)
EFFECTS_CLASSES=(AdjustmentsTest ModifierNodeInstantiationTest)

GROUP_NAMES=(Core Tools)
for cat in "${EFFECTS_CATEGORIES[@]}"; do GROUP_NAMES+=("Render-$cat"); done
for cls in "${EFFECTS_CLASSES[@]}"; do GROUP_NAMES+=("$cls"); done

groups=()
if [[ ${1:-} == --only ]]; then
	shift
	IFS=',' read -r -a wanted <<< "$1"
	for want in "${wanted[@]}"; do
		matched=""
		for name in "${GROUP_NAMES[@]}"; do
			if [[ ${name,,} == *"${want,,}"* ]]; then matched="$name"; break; fi
		done
		if [[ -z $matched ]]; then
			echo "No group matches '$want'. Groups:"
			printf '  %s\n' "${GROUP_NAMES[@]}"
			exit 2
		fi
		groups+=("$matched")
	done
else
	groups=("${GROUP_NAMES[@]}")
fi

echo "Building affected test projects..."
builds=(tests/Pinta.Effects.Tests)
[[ " ${groups[*]} " == *" Core "* ]] && builds+=(tests/Pinta.Core.Tests)
[[ " ${groups[*]} " == *" Tools "* ]] && builds+=(tests/Pinta.Tools.Tests)
printf '%s\n' "${builds[@]}" | sort -u | xargs -n1 dotnet build > /dev/null || exit 1

# The Cells category alone is the suite's critical path (13 per-pixel tests, ~55s
# in one process). Partition its test list into chunks and run each chunk in its
# own process - a chunk is an OR of exact `Name=` terms. `~` (Contains) would be
# wrong here: `~Cells1` also matches Cells10-13, so those run in two chunks each.
# Listed fresh every run, so added Cells tests always land in some chunk.
CELLS_SPLIT=2
cells_chunks=()
if [[ " ${groups[*]} " == *" Render-Cells "* ]]; then
	mapfile -t cells_tests < <(
		dotnet test tests/Pinta.Effects.Tests --no-build --list-tests --filter "TestCategory=Cells" 2>/dev/null |
			grep -oE "    [A-Za-z0-9_]+$" | sed 's/^ *//' | sort -u
	)
	if [[ ${#cells_tests[@]} -eq 0 ]]; then
		cells_chunks=("") # listing failed: fall back to one unsplit run
	else
		for ((c = 0; c < CELLS_SPLIT; c++)); do
			chunk=()
			for ((t = c; t < ${#cells_tests[@]}; t += CELLS_SPLIT)); do chunk+=("Name=${cells_tests[$t]}"); done
			# A | separated list of exact-name terms; | cannot appear in C# identifiers so it is unambiguous.
			cells_chunks+=("$(IFS='|'; echo "${chunk[*]}")")
		done
	fi
fi

log_dir=$(mktemp -d)
pids=()
names=()

launch() { # launch <name> <project> <filter...>
	local name=$1 project=$2
	shift 2
	dotnet test "$project" --no-build "$@" > "$log_dir/$name.log" 2>&1 &
	pids+=($!)
	names+=("$name")
}

run_core_tools() { # run_core_tools <Core|Tools>
	local proj="tests/Pinta.$1.Tests"
	launch "$1" "$proj"
}

for group in "${groups[@]}"; do
	case $group in
		Core | Tools) run_core_tools "$group" ;;
		Render-Cells)
			for c in "${!cells_chunks[@]}"; do
				filter="TestCategory=Cells"
				if [[ -n ${cells_chunks[$c]} ]]; then
					filter="(${cells_chunks[$c]})&TestCategory=Cells"
				fi
				launch "Render-Cells.$c" tests/Pinta.Effects.Tests --filter "$filter"
			done
			;;
		Render-*) launch "Render-${group#Render-}" tests/Pinta.Effects.Tests --filter "TestCategory=${group#Render-}" ;;
		*) launch "$group" tests/Pinta.Effects.Tests --filter "FullyQualifiedName~$group" ;;
	esac
done

status=0
failing=""
for i in "${!names[@]}"; do
	if ! wait "${pids[$i]}"; then
		status=1
		failing="$failing ${names[$i]}"
	fi
done
for name in "${names[@]}"; do
	line=$(grep -hE "Passed!|Failed!" "$log_dir/$name.log" | tail -n1)
	flag="  "
	[[ " $failing " == *" $name "* ]] && flag="❌ "
	printf '%s%-28s %s\n' "$flag" "$name" "${line:-NO OUTPUT (see below)}"
done

if [[ -n $failing ]]; then
	echo
	echo "=== Failing group output ==="
	for name in $failing; do
		echo "--- $name ---"
		cat "$log_dir/$name.log"
	done
fi

rm -rf "$log_dir"

if [[ $status == 0 ]]; then echo "ALL GREEN"; else echo "FAILURES PRESENT"; fi
exit $status
