BINARY = matrix-bg
SOURCE = matrix-bg.swift
MENUBAR_SOURCE = matrix-bg-menubar.swift
INSTALL_DIR = $(HOME)/.local/bin
APP_BUNDLE = MatrixBG.app

.PHONY: build install app app-install clean

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

clean:
	rm -f $(BINARY)
	rm -rf $(APP_BUNDLE)
