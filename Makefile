BINARY = matrix-bg
SOURCE = matrix-bg.swift
INSTALL_DIR = $(HOME)/.local/bin

.PHONY: build install clean

build:
	swiftc -O -o $(BINARY) $(SOURCE) -framework AppKit -framework CoreText

install: build
	mkdir -p $(INSTALL_DIR)
	cp $(BINARY) $(INSTALL_DIR)/$(BINARY)
	chmod +x $(INSTALL_DIR)/$(BINARY)
	@echo "Installed to $(INSTALL_DIR)/$(BINARY)"

clean:
	rm -f $(BINARY)
