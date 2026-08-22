BINARY = matrix-bg
SOURCE = matrix-bg.swift
MENUBAR_SOURCE = matrix-bg-menubar.swift
INSTALL_DIR = $(HOME)/.local/bin
APP_BUNDLE = MatrixBG.app
WIN_SOURCE = matrix-bg-windows.c
WIN_BINARY = matrix-bg.exe
WIN_CC = x86_64-w64-mingw32-gcc

.PHONY: build install app app-install windows clean

build:
	swiftc -O -o $(BINARY) $(SOURCE) -framework AppKit -framework CoreText

install: build
	mkdir -p $(INSTALL_DIR)
	cp $(BINARY) $(INSTALL_DIR)/$(BINARY)
	chmod +x $(INSTALL_DIR)/$(BINARY)
	@echo "Installed to $(INSTALL_DIR)/$(BINARY)"

app:
	./build-app.sh

app-install: app
	rm -rf /Applications/$(APP_BUNDLE)
	cp -R $(APP_BUNDLE) /Applications/
	@echo "Installed to /Applications/$(APP_BUNDLE)"

# Cross-compile the Windows build (brew install mingw-w64)
windows:
	$(WIN_CC) -O2 -Wall -Wextra -municode -mwindows $(WIN_SOURCE) -o $(WIN_BINARY) -lgdi32 -luser32
	x86_64-w64-mingw32-strip $(WIN_BINARY)
	@echo "Built $(WIN_BINARY)"

# Prove the Windows port's simulation obeys the Swift app's rules: compiles the
# shipped matrix-bg-windows.c natively via test/windows.h shims and executes it.
sim-test:
	cc -O2 -Wall -Itest test/sim-parity-test.c -o test/sim-parity-test
	./test/sim-parity-test
	./test/sim-parity-test charset > test/.charset-c.txt
	swift test/charset.swift > test/.charset-swift.txt
	diff -u test/.charset-c.txt test/.charset-swift.txt
	@rm -f test/.charset-c.txt test/.charset-swift.txt
	@echo "Charset identical between Swift and Windows builds"

clean:
	rm -f $(BINARY) $(WIN_BINARY)
	rm -rf $(APP_BUNDLE)
