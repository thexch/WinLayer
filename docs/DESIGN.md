# Design de WinLayer V2

Trois directions ont été comparées avant l’implémentation. Une composition éditoriale claire aurait donné beaucoup d’espace aux données, avec une identité visuelle discrète. Un instrument audio sombre aurait donné un caractère plus technique, au risque de durcir la lecture de réglages courants. Le verre optique stratifié a été retenu pour reprendre les couches du logo, créer une illustration propre à WinLayer et conserver une navigation calme dans les deux thèmes.

L’aperçu donne le premier plan au module actif dans une composition asymétrique. Le motif de verre est construit en CSS et reste immobile. Les préférences occupent des lignes compactes et les résultats Bluetooth affichent une jauge seulement si un niveau a réellement été lu. Les états déconnecté, inconnu et service indisponible portent des libellés distincts. Sur les fenêtres étroites, la navigation devient un rail d’icônes nommées pour le lecteur d’écran.

| Élément | Règle |
| --- | --- |
| Couleurs | Fond pervenche pâle, surfaces bleutées, accent violet et rail encre. Les états positifs et bas utilisent aussi du texte et des chiffres. |
| Typographie | Manrope Variable pour les écrans et les contrôles. Geist Mono pour les pourcentages, heures et versions. Les deux sont fournies localement. |
| Surfaces | Le module principal porte l’illustration. Les détails utilisent des séparateurs, tandis que les groupes de réglages ont une bordure légère. |
| Interaction | Navigation immédiate. Interrupteurs courts et retour d’état. Confirmation Radix avec focus sur Annuler et fermeture via Échap. |
| Motion | Mouvement unique de 180 ms à l’ouverture de la confirmation, rotation ponctuelle au rafraîchissement. La préférence de mouvement réduit les supprime. |
| Accessibilité | Navigation clavier, focus visible, lien d’évitement, labels pour les icônes, retour d’action annoncé et mode contraste élevé. |

Les variables CSS dans `web/src/styles.css` sont la source des couleurs et des styles. Les composants partagés de contrôle restent dans `web/src/controls.tsx`. Le module de confirmation charge Radix et Motion à la demande. Le kit shadcn/ui serait redondant pour ces composants, avec des primitives natives et Radix qui couvrent les besoins de la V2.
