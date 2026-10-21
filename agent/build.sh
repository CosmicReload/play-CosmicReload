#!/usr/bin/env bash
# Builds build/cosmic-agent.jar (agent + ASM bundled). Needs a JDK 8+ and asm-9.x.jar (set ASM_JAR, or one is
# looked up in the Gradle cache).
set -e
cd "$(dirname "$0")"
JAVA_BIN="${COSMIC_JAVA8:-/c/Program Files/Eclipse Adoptium/jdk-8.0.462.8-hotspot}/bin"
if [ -z "$ASM_JAR" ]; then
  ASM_JAR=$(find ~/.gradle -name "asm-9.*.jar" ! -name "*sources*" 2>/dev/null | head -1)
fi
[ -f "$ASM_JAR" ] || { echo "ASM 9 jar not found; set ASM_JAR"; exit 1; }

rm -rf build && mkdir -p build/classes
"$JAVA_BIN/javac" -source 1.8 -target 1.8 -nowarn -cp "$ASM_JAR" -d build/classes $(find src -name "*.java")
(cd build/classes && "$JAVA_BIN/jar" xf "$ASM_JAR" org && rm -rf META-INF)
cp ../LICENSE-ASM.txt build/classes/
"$JAVA_BIN/jar" cfm build/cosmic-agent.jar manifest.txt -C build/classes .
echo "built build/cosmic-agent.jar"
