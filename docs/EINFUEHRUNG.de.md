# Was SocketScope leistet – und was nicht

OeXYZ SocketScope ist ein lokales Diagnoseprogramm für Windows und Linux. Es fasst
Informationen zusammen, die sonst auf mehrere Werkzeuge und Betriebssystemansichten
verteilt sind: offene Netzwerkports, aktive TCP-Verbindungen, UDP-Endpunkte, Prozesse,
Dienste, Containerhinweise, lokale Interfaces und dazu möglicherweise passende
Firewall-Regeln.

Die Anwendung ist vor allem dann nützlich, wenn die einfache Frage „Warum ist dieser
Port offen?“ nicht mehr einfach zu beantworten ist. SocketScope verbindet einen
Netzwerkendpunkt mit dem Prozess und – soweit das Betriebssystem genügend Informationen
liefert – mit einer ausführbaren Datei, einem Windows- oder systemd-Dienst und einem
Container. Die Live-Ansicht lässt sich durchsuchen, filtern, sortieren und pausieren.
Neue, verschwundene oder veränderte Einträge werden sichtbar; gespeicherte Snapshots
können später miteinander verglichen werden.

## Was SocketScope gut kann

- TCP-Verbindungen, TCP-Listener und UDP-Endpunkte für IPv4 und IPv6 gemeinsam
  darstellen.
- Zeigen, welcher Prozess und welche PID einen Endpunkt verwenden, soweit die lokalen
  Rechte und das Betriebssystem dies zulassen.
- Bindings verständlich unterscheiden: nur Loopback, bestimmtes Interface,
  LAN-Kontext oder alle passenden lokalen Interfaces.
- Verfügbare Prozess-, Dienst-, Container- und lokale Firewall-Hinweise getrennt von
  bloßen Ableitungen oder unbekannten Informationen darstellen.
- Veränderungen während einer Sitzung sowie Unterschiede zwischen zwei lokalen
  Snapshots nachvollziehbar machen.
- Dieselbe Engine über eine grafische Oberfläche und den Terminalbefehl `socketscope`
  bereitstellen.
- Den Terminalbefehl direkt unter **Einstellungen → Terminalbefehl** benutzerlokal
  installieren oder aktualisieren und seinen PATH-Status anzeigen.
- Vollständige HTML-/JSON-Berichte erzeugen oder mit „Share-safe“ eine reduzierte
  Supportkopie erstellen.
- Ohne Benutzerkonto, Werbung, Telemetrie oder dauerhaften Hintergrunddienst arbeiten.

## Was SocketScope ausdrücklich nicht kann

SocketScope ist kein Portscanner, kein Paketmitschnitt, keine Firewall und kein
automatischer Sicherheitsbewerter. Es beendet keine Prozesse, verändert keine Dienste
und legt keine Firewall-Regeln an.

Besonders wichtig ist die Unterscheidung zwischen einem lokalen Binding und einer
extern bestätigten Erreichbarkeit. Ein Listener auf `0.0.0.0` oder `::` akzeptiert
Verbindungen über alle dazu passenden lokalen Interfaces. Daraus folgt nicht, dass der
Port aus dem Internet erreichbar ist. Router, NAT, VPN, Cloud-Regeln, vorgeschaltete
Firewalls und das tatsächlich sendende Netzwerk wurden von SocketScope nicht geprüft.
Die Anwendung zeigt diesen Zustand deshalb als „External reachability: Not tested“.

Auch lokale Zuordnungen können unvollständig sein. Ein Prozess kann während der
Abfrage enden, Berechtigungen können den Pfad verbergen, PID-Werte können
wiederverwendet werden und Container- oder Firewall-Konfigurationen können komplexer
sein als die lokal verfügbare Evidenz. In diesen Fällen zeigt SocketScope „Unknown“,
„Unavailable“ oder eine konkrete Einschränkung, statt eine sichere Antwort zu
simulieren.

## Typische Einsatzfälle

- Ein Entwickler möchte wissen, welches lokale Programm Port 8080 oder 5432 belegt.
- Ein Administrator untersucht, warum ein Dienst nur auf `localhost` oder auf allen
  Interfaces lauscht.
- Nach einer Softwareinstallation soll geprüft werden, welche Listener neu
  hinzugekommen sind.
- Ein Supportteam benötigt einen reproduzierbaren Snapshot statt eines unvollständigen
  Bildschirmfotos.
- In einem Homelab soll nachvollzogen werden, welcher Prozess oder Container hinter
  einem lokal geöffneten Port steckt.
- Ein Skript oder Terminalworkflow benötigt dieselben Daten über
  `socketscope list`, `watch`, `snapshot` oder `report`.

## Full report und Share-safe report

Ein **Full report** enthält die verfügbaren Diagnosedaten vollständig. Dazu können
private oder öffentliche IP-Adressen, PIDs, Benutzernamen in Pfaden sowie Prozess-,
Dienst- und Containerbezeichnungen gehören. Er eignet sich für die eigene Analyse oder
einen bewusst kontrollierten Empfängerkreis.

Ein **Share-safe report** verwendet die unter **Einstellungen → Exportvorgaben**
gewählten Feldkategorien. Standardmäßig ersetzt er IP-Adressen außerhalb von Loopback-
und Wildcard-Adressen, PIDs, Beobachtungskennungen, Benutzernamen in typischen
Home-Pfaden, Containerkennungen und Firewall-Regelnamen. Prozess- und Dienstnamen
bleiben zunächst erhalten, können aber ebenfalls ausgewählt werden. Ports und
Binding-Kategorien bleiben immer verfügbar. Zusätzlich können bis zu 32 eigene
sensible Klartextwerte eingetragen werden. Sie werden nur wörtlich und ohne Beachtung
der Groß-/Kleinschreibung ersetzt; Regex oder Skripte werden nicht ausgeführt. Die
Klartexte bleiben im geschützten lokalen Settings-Store und werden nicht in den Bericht
geschrieben. Die Vorschau zeigt vor dem Speichern, wie viele Werte je Kategorie und wie
viele eigene Vorkommen betroffen wären. „Share-safe“ bedeutet weiterhin nicht
„garantiert anonym“; der Bericht muss vor einer öffentlichen Weitergabe geprüft werden.

## Kurzfassung

SocketScope beantwortet zuverlässig, was der lokale Rechner zum Zeitpunkt der
Erfassung über seine Netzwerkendpunkte erkennen lässt. Es macht Unsicherheit sichtbar
und trennt lokale Hinweise von einer nicht durchgeführten externen Prüfung. Genau darin
liegt sein Nutzen: weniger Rohdaten und Vermutungen, dafür eine nachvollziehbare lokale
Bestandsaufnahme.
